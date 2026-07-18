namespace Modulus.Testing.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

/// <summary>A minimal module context used to exercise the SQLite swap.</summary>
internal sealed class WidgetDbContext(
    DbContextOptions options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "wdg_";

    public DbSet<Widget> Widgets => Set<Widget>();
}

/// <summary>A plain entity — no soft-delete or tenant filter to keep the schema minimal.</summary>
internal sealed class Widget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
