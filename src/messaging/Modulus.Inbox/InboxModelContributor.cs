namespace Modulus.Inbox;

using Microsoft.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.ModelBuilding;
using Modulus.Inbox.Configurations;

/// <summary>
/// Maps the <see cref="Modulus.Inbox.Abstractions.InboxMessage"/> entity into
/// every module context, so <c>AddInbox&lt;TContext&gt;</c> works without the
/// app hand-wiring <see cref="InboxMessageConfiguration"/>. Without this
/// contributor the EF inbox could not persist claims — the entity had no
/// table.
/// </summary>
internal sealed class InboxModelContributor : IModuleModelContributor
{
    public void Contribute(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
}
