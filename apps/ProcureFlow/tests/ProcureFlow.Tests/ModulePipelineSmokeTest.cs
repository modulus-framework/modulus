using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Modulus.AspNetCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Testing;
using ProcureFlow.Api.Modules;

namespace ProcureFlow.Tests;

/// <summary>
/// Smoke test that boots the Modulus module pipeline and resolves every
/// registered module DbContext, verifying the full module composition graph
/// (each module's DbContext options + DI registrations) is wired correctly.
/// </summary>
public sealed class AppSmokeTest
{
    [Fact]
    public void ModulePipeline_BootsAndResolvesModuleDbContexts()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddModulus<ProcureFlowHostModule>(builder.Configuration);
        builder.Services.AddMediator();
        builder.Services.AddModulusEvents(typeof(ProcureFlowHostModule).Assembly);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        // Each module registers its own DbContext via AddModuleDatabase.
        // Resolving them all constructs the contexts (exercising options +
        // ctor deps) — any misconfiguration throws and fails the test.
        var contexts = scope.ServiceProvider
            .GetServices<DbContext>()
            .ToList();

        Assert.NotEmpty(contexts);
    }
}

/// <summary>
/// HTTP integration tests that drive the real host through
/// <see cref="ModulusWebAppFactory{TEntryPoint}"/>: the full middleware
/// pipeline (correlation, idempotency, …) is active and every module DbContext
/// runs against an isolated in-memory SQLite database — no real database needed.
/// </summary>
public sealed class ApiIntegrationTests(ModulusWebAppFactory<Program> factory)
    : IClassFixture<ModulusWebAppFactory<Program>>
{
    [Fact]
    public async Task Health_Live_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

}
