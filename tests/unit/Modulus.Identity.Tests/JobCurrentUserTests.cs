using System.Collections.Concurrent;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.BackgroundJobs.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Inside a background job, the identity's <see cref="ICurrentUser"/> is the person who queued the job, with the roles the identity store gave
/// when the job started. A job queued with no person behind it has no caller.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobCurrentUserTests
{
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public sealed record CallerProbeArgs(int Id);

    public sealed class CallerProbeJob(ICurrentUser user, ConcurrentQueue<string> seen) : IBackgroundJob<CallerProbeArgs>
    {
        public Task ExecuteAsync(CallerProbeArgs args, CancellationToken ct)
        {
            seen.Enqueue($"{user.UserId}|{user.IsAuthenticated}|{user.IsInRole("Buyers")}");
            return Task.CompletedTask;
        }
    }

    private sealed class RolesOf : IUserRoleDirectory
    {
        private readonly Dictionary<Guid, string[]> _roles = [];

        public void Set(Guid userId, params string[] roles) => _roles[userId] = roles;

        public ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyCollection<string>?>(_roles.TryGetValue(userId, out var roles) ? roles : null);
    }

    private static ServiceProvider BuildProvider(RolesOf directory, ConcurrentQueue<string> seen)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>();
        services.AddSingleton<IUserRoleDirectory>(directory);
        services.AddSingleton(seen);
        services.AddBackgroundJobs(typeof(JobCurrentUserTests).Assembly);
        return services.BuildServiceProvider();
    }

    private static async Task<string> RunProbeAsync(ServiceProvider provider, ConcurrentQueue<string> seen, Action<IHttpContextAccessor> beforeEnqueue)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
        try
        {
            var accessor = provider.GetRequiredService<IHttpContextAccessor>();
            beforeEnqueue(accessor);
            await provider.GetRequiredService<IJobScheduler>().EnqueueAsync<CallerProbeJob, CallerProbeArgs>(new CallerProbeArgs(1));
            accessor.HttpContext = null;

            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (true)
            {
                if (seen.TryDequeue(out var result))
                    return result;
                if (DateTimeOffset.UtcNow > deadline)
                    throw new TimeoutException("The probe job did not run.");
                await Task.Delay(20);
            }
        }
        finally
        {
            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_job_sees_the_person_who_queued_it_and_the_roles_the_store_gives_when_it_starts()
    {
        var directory = new RolesOf();
        directory.Set(Bob, "Buyers");
        var seen = new ConcurrentQueue<string>();
        await using var provider = BuildProvider(directory, seen);

        var result = await RunProbeAsync(provider, seen, accessor => accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Bob.ToString())], "test")),
        });

        result.Should().Be($"{Bob}|True|True");
    }

    [Fact]
    public async Task A_job_queued_with_no_person_behind_it_has_no_caller()
    {
        var seen = new ConcurrentQueue<string>();
        await using var provider = BuildProvider(new RolesOf(), seen);

        var result = await RunProbeAsync(provider, seen, _ => { });

        result.Should().Be("|False|False");
    }
}
