namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Core.Null;
using Modulus.Data.PostgreSQL;
using Modulus.Data.SqlServer;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Events;
using Xunit;

/// <summary>
/// The per-tenant helpers of the PostgreSQL and SQL Server packages register their provider's probe and pool clear, so a rotated
/// tenant string can be probed and its pool cleared.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PerTenantProviderHelperTests
{
    [Fact]
    public async Task The_PostgreSQL_helper_registers_its_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new NullCurrentTenant());
        services.AddPostgreSqlPerTenantDatabase<NotesDbContext>("primary", "Host=localhost;Database=host");

        var providers = services.BuildServiceProvider().GetRequiredService<ITenantDatabaseProviders>();

        // The probe needs the registered provider; an unreachable server fails, but not for want of a provider.
        var error = await Record.ExceptionAsync(() => providers.ProbeAsync("postgresql", "Host=127.0.0.1;Port=1;Database=tenant_a;Timeout=1"));
        error?.Message.Should().NotContain("No tenant database provider");
    }

    [Fact]
    public async Task The_SQL_Server_helper_registers_its_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new NullCurrentTenant());
        services.AddSqlServerPerTenantDatabase<NotesDbContext>("primary", "Server=localhost;Database=host");

        var providers = services.BuildServiceProvider().GetRequiredService<ITenantDatabaseProviders>();

        var error = await Record.ExceptionAsync(() => providers.ProbeAsync("sqlserver", "Server=127.0.0.1,1;Database=tenant_a;Connect Timeout=1"));
        error?.Message.Should().NotContain("No tenant database provider");
    }

    [Fact]
    public async Task An_unregistered_provider_is_still_refused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new NullCurrentTenant());
        services.AddPostgreSqlPerTenantDatabase<NotesDbContext>("primary", "Host=localhost;Database=host");

        var providers = services.BuildServiceProvider().GetRequiredService<ITenantDatabaseProviders>();

        Func<Task> act = () => providers.ProbeAsync("oracle", "Host=localhost");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No tenant database provider*");
    }

    private sealed class NotesDbContext(
        DbContextOptions<NotesDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "notes_";
    }
}
