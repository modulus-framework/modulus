using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Modulus.EntityFrameworkCore;

/// <summary>
/// Generates Guid v7 keys on the client. The time prefix makes the keys sort by creation time, which is what keyset paging
/// needs. Used only on PostgreSQL, whose <c>uuid</c> ordering is bytewise in RFC order; SQL Server orders
/// <c>uniqueidentifier</c> differently, so the framework does not use this generator there.
/// </summary>
public sealed class GuidV7ValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => global::Modulus.GuidV7.Create();
}
