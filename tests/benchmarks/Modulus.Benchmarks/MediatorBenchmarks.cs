using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Extensions;

namespace Modulus.Benchmarks;

public sealed record Ping(int Value) : ICommand<int>;

public sealed class PingHandler : ICommandHandler<Ping, int>
{
    public Task<int> HandleAsync(Ping command, CancellationToken ct) => Task.FromResult(command.Value);
}

/// <summary>One mediator send through the default pipeline (logging, validation, authorization, caching, ...).</summary>
[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private ServiceProvider _root = null!;
    private IServiceScope _scope = null!;
    private IMediator _mediator = null!;
    private readonly Ping _ping = new(42);

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddMediator(o => o.RegisterServicesFromAssembly(typeof(MediatorBenchmarks).Assembly));
        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();
        _mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _root.Dispose();
    }

    [Benchmark]
    public Task<int> Send() => _mediator.SendAsync(_ping, CancellationToken.None);
}
