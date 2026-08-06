namespace Modulus.Testing.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

/// <summary>A minimal module context used to exercise the SQLite swap.</summary>
internal sealed class WidgetDbContext(
    DbContextOptions<WidgetDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "wdg_";

    public DbSet<Widget> Widgets => Set<Widget>();
}

/// <summary>A second minimal module context: multi-module apps have several.
/// Typed <c>DbContextOptions&lt;TContext&gt;</c> ctor — the pattern the generated
/// <c>{Module}DbContext</c> uses; EF Core cannot disambiguate multiple contexts
/// from a non-generic <c>DbContextOptions</c> ctor.</summary>
internal sealed class GadgetDbContext(
    DbContextOptions<GadgetDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "gdg_";

    public DbSet<Gadget> Gadgets => Set<Gadget>();
}

/// <summary>A plain entity — no soft-delete or tenant filter to keep the schema minimal.</summary>
internal sealed class Widget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Entity for the second context above.</summary>
internal sealed class Gadget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
