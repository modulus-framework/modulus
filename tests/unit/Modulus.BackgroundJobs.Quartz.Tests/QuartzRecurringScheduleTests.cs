using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl;
using Xunit;

namespace Modulus.BackgroundJobs.Quartz.Tests;

/// <summary>
/// <c>AddRecurring</c> is fire-and-forget, so a failed schedule cannot reach the caller. It must still be survivable (a transient store
/// error is retried) and loud (a schedule that never took hold logs at Critical).
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuartzRecurringScheduleTests
{
    public sealed record Args;

    public sealed class NoopJob : IBackgroundJob<Args>
    {
        public Task ExecuteAsync(Args args, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingLogger : ILogger<QuartzJobScheduler>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Levels)
                Levels.Add(logLevel);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static async Task<bool> WaitAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (await condition())
                return true;
            await Task.Delay(50);
        }

        return false;
    }

    [Fact]
    public async Task An_invalid_cron_expression_logs_critical_and_schedules_nothing()
    {
        var scheduler = await new StdSchedulerFactory().GetScheduler();
        var factory = Substitute.For<ISchedulerFactory>();
        factory.GetScheduler(Arg.Any<CancellationToken>()).Returns(scheduler);
        var logger = new CapturingLogger();
        var sut = new QuartzJobScheduler(factory, Substitute.For<IServiceProvider>(), logger);

        sut.AddRecurring<NoopJob, Args>("bad-cron", "not a cron", new Args());

        (await WaitAsync(() => Task.FromResult(logger.Levels.Contains(LogLevel.Critical)), TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await scheduler.CheckExists(new JobKey("bad-cron", "recurring"))).Should().BeFalse();
        await scheduler.Shutdown();
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_the_job_is_scheduled()
    {
        var scheduler = await new StdSchedulerFactory().GetScheduler();
        var factory = Substitute.For<ISchedulerFactory>();
        var calls = 0;
        factory.GetScheduler(Arg.Any<CancellationToken>()).Returns(_ =>
            ++calls == 1 ? Task.FromException<IScheduler>(new InvalidOperationException("store unavailable")) : Task.FromResult(scheduler));
        var logger = new CapturingLogger();
        var sut = new QuartzJobScheduler(factory, Substitute.For<IServiceProvider>(), logger);

        sut.AddRecurring<NoopJob, Args>("retry-me", "0 0 * * * ?", new Args());

        (await WaitAsync(() => scheduler.CheckExists(new JobKey("retry-me", "recurring")), TimeSpan.FromSeconds(10))).Should().BeTrue();
        logger.Levels.Should().Contain(LogLevel.Warning).And.NotContain(LogLevel.Critical);
        await scheduler.Shutdown();
    }
}
