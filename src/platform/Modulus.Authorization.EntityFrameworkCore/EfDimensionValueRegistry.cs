using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Dimensions;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// The codes of each scope dimension in the authorization database (<c>ModulusDimensionValues</c>), per company. Registration is
/// idempotent on (dimension, code): the first registration creates the value, later ones return it unchanged. Codes are compared in
/// canonical form (<see cref="DimensionKeys"/>), so the company's warehouse <c>wh-01</c> is the same value as <c>WH-01</c>.
/// </summary>
public sealed class EfDimensionValueRegistry(IDbContextFactory<AuthorizationStoreDbContextBase> factory) : IDimensionValueRegistry
{
    /// <inheritdoc />
    public async Task<DimensionValue> RegisterAsync(string dimension, string code, string label, CancellationToken ct = default)
    {
        var name = DimensionKeys.Dimension(dimension);
        var key = DimensionKeys.Code(code);
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.DimensionValues.SingleOrDefaultAsync(v => v.Dimension == name && v.Code == key, ct);
        if (row is null)
        {
            // The label defaults to the code, so a value is never shown blank.
            row = new DimensionValueRow
            {
                Id = Guid.NewGuid(),
                Dimension = name,
                Code = key,
                Label = string.IsNullOrWhiteSpace(label) ? key : label.Trim(),
            };
            db.DimensionValues.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent registration of the same code won the unique index. Its value is the one the code now has;
                // the failed context is not reused, so the winner is read from a fresh one.
                var winner = await FindRowAsync(factory, name, key, ct);
                if (winner is null)
                    throw;
                return ToValue(winner);
            }
        }

        return ToValue(row);
    }

    private static async Task<DimensionValueRow?> FindRowAsync(
        IDbContextFactory<AuthorizationStoreDbContextBase> factory, string name, string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.DimensionValues.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Dimension == name && v.Code == key, ct);
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveIdAsync(string dimension, string code, CancellationToken ct = default)
    {
        var name = DimensionKeys.Dimension(dimension);
        var key = DimensionKeys.Code(code);
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.DimensionValues.AsNoTracking()
            .Where(v => v.Dimension == name && v.Code == key)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DimensionValue>> ListAsync(string dimension, CancellationToken ct = default)
    {
        var name = DimensionKeys.Dimension(dimension);
        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.DimensionValues.AsNoTracking()
            .Where(v => v.Dimension == name)
            .OrderBy(v => v.Code)
            .ToListAsync(ct);
        return [.. rows.Select(ToValue)];
    }

    /// <inheritdoc />
    public async Task<DimensionValue> SetParentAsync(
        string dimension, string code, string? parentDimension, string? parentCode, CancellationToken ct = default)
    {
        var name = DimensionKeys.Dimension(dimension);
        var key = DimensionKeys.Code(code);
        await using var db = await factory.CreateDbContextAsync(ct);

        var child = await db.DimensionValues.SingleOrDefaultAsync(v => v.Dimension == name && v.Code == key, ct)
            ?? throw new NotFoundException($"No {name} value '{key}' is registered.");

        Guid? parentId = null;
        if (parentDimension is not null || parentCode is not null)
        {
            var parentName = DimensionKeys.Dimension(parentDimension ?? "");
            var parentKey = DimensionKeys.Code(parentCode ?? "");
            var parent = await db.DimensionValues.AsNoTracking()
                .SingleOrDefaultAsync(v => v.Dimension == parentName && v.Code == parentKey, ct)
                ?? throw new NotFoundException($"No {parentName} value '{parentKey}' is registered.");

            if (parent.Id == child.Id)
                throw new ArgumentException("A dimension value cannot be placed under itself.");
            await RefuseLoopAsync(db, child.Id, parent.Id, ct);
            parentId = parent.Id;
        }

        child.ParentId = parentId;
        await db.SaveChangesAsync(ct);
        return ToValue(child);
    }

    // Walks up from the proposed parent: reaching the child again means the placement would close a loop. The walk is bounded, so a
    // corrupt chain cannot run forever; the bound is far above any real hierarchy.
    private static async Task RefuseLoopAsync(AuthorizationStoreDbContextBase db, Guid child, Guid proposedParent, CancellationToken ct)
    {
        var cursor = proposedParent;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var up = await db.DimensionValues.AsNoTracking()
                .Where(v => v.Id == cursor)
                .Select(v => v.ParentId)
                .FirstOrDefaultAsync(ct);
            if (up is null)
                return;
            if (up == child)
                throw new ArgumentException("That placement would put the value under one of its own children.");
            cursor = up.Value;
        }

        throw new ArgumentException("The dimension hierarchy is deeper than the supported limit.");
    }

    /// <summary>The deepest placement the walk follows.</summary>
    internal const int MaxDepth = 64;

    private static DimensionValue ToValue(DimensionValueRow row)
        => new(row.Id, row.Dimension, row.Code, row.Label, row.ParentId);
}
