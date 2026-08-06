using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Extensions;
using Xunit;

namespace Modulus.Mediator.Tests;

[Trait("Category", "Unit")]
public sealed class MediatorTests
{
    private IMediator BuildMediator(Action<MediatorOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The default-on pipeline behaviors need lightweight infra:
        //   - CachingBehavior  -> IMemoryCache
        //   - AuthorizationBehavior -> ICurrentUser (NullCurrentUser: denies,
        //     but the test commands carry no [RequirePermission], so it no-ops)
        // TransactionBehavior no-ops when no DbContext is registered.
        services.AddMemoryCache();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddMediator(opts =>
        {
            opts.RegisterServicesFromAssembly(GetType().Assembly);
            configure?.Invoke(opts);
        });
        return services.BuildServiceProvider()
            .GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task SendAsync_WithValidCommand_ReturnsResult()
    {
        var mediator = BuildMediator();
        var result = await mediator.SendAsync(new PingCommand());
        result.Should().Be("pong");
    }

    [Fact]
    public async Task QueryAsync_ReturnsExpectedValue()
    {
        var mediator = BuildMediator();
        var result = await mediator.QueryAsync(new EchoQuery("hello"));
        result.Should().Be("hello");
    }

    // ── Test doubles ─────────────────────────────────────────────
    public record PingCommand : ICommand<string>;
    public sealed class PingHandler : ICommandHandler<PingCommand, string>
    {
        public Task<string> HandleAsync(PingCommand _, CancellationToken ct)
            => Task.FromResult("pong");
    }

    public record EchoQuery(string Value) : IQuery<string>;
    public sealed class EchoHandler : IQueryHandler<EchoQuery, string>
    {
        public Task<string> HandleAsync(EchoQuery q, CancellationToken ct)
            => Task.FromResult(q.Value);
    }
}
