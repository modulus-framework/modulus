namespace Modulus.AspNetCore.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.AspNetCore.Extensions;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary>
/// Modules used to shut down in <c>StoppingAsync</c>, which runs before any hosted service stops: a module released
/// its resources while the server was still draining requests and background workers (the outbox poller, job
/// queues) still used them. They now shut down in <c>StoppedAsync</c>, after every hosted service has stopped.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModuleLifecycleHostedServiceTests
{
    [Fact]
    public async Task Modules_shut_down_after_every_hosted_service_has_stopped()
    {
        var events = new ConcurrentQueue<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IModuleLoader>(new RecordingLoader(events));
        builder.Services.AddHostedService<ModuleLifecycleHostedService>();
        builder.Services.AddHostedService(_ => new RecordingWorker(events));

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        events.Should().Equal("modules initialized", "worker started", "worker stopped", "modules shut down");
    }

    private sealed class RecordingWorker(ConcurrentQueue<string> events) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            events.Enqueue("worker started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Enqueue("worker stopped");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLoader(ConcurrentQueue<string> events) : IModuleLoader
    {
        public IReadOnlyList<ModuleDescriptor> GetDescriptors() => [];

        public Task InitializeAllAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            events.Enqueue("modules initialized");
            return Task.CompletedTask;
        }

        public Task ShutdownAllAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            events.Enqueue("modules shut down");
            return Task.CompletedTask;
        }
    }
}
