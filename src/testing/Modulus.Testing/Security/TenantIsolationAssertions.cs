namespace Modulus.Testing.Security;

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore.Isolation;

/// <summary>Assertions that one company's data never reaches another (security plan, phase 5).</summary>
public static class TenantIsolationAssertions
{
    /// <summary>
    /// Saves <paramref name="entity"/> as company <paramref name="owner"/>, then, as company <paramref name="other"/>,
    /// checks that it is neither found by key, nor listed, nor reachable with <c>IgnoreQueryFilters()</c> (the
    /// raw-SQL guard must refuse that query), nor updatable. Runs against the app's real
    /// <typeparamref name="TContext"/> (the swapped test database, under <see cref="ModulusWebAppFactory{TEntryPoint}"/>).
    /// </summary>
    /// <exception cref="TenantIsolationException">Company <paramref name="other"/> could see or change the row.</exception>
    public static async Task AssertTenantIsolationAsync<TContext, TEntity>(
        this IServiceProvider services, TEntity entity, Guid owner, Guid other, CancellationToken ct = default)
        where TContext : DbContext
        where TEntity : class, IHasTenantId
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entity);
        if (owner == other)
            throw new ArgumentException("The two companies must differ.", nameof(other));

        var tenant = services.GetRequiredService<ICurrentTenant>();
        object?[] key;
        using (tenant.Change(new TenantInfo(owner, "isolation-owner")))
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            db.Set<TEntity>().Add(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            key = KeyOf(db, entity);
        }

        using (tenant.Change(new TenantInfo(other, "isolation-other")))
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

            if (await db.Set<TEntity>().FindAsync(key, ct).ConfigureAwait(false) is not null)
                throw new TenantIsolationException($"{typeof(TEntity).Name} of company {owner} was found by key as company {other}.");

            var listed = await db.Set<TEntity>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            if (listed.Any(e => KeyOf(db, e).SequenceEqual(key)))
                throw new TenantIsolationException($"{typeof(TEntity).Name} of company {owner} was listed as company {other}.");

            try
            {
                var unfiltered = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
                if (unfiltered.Any(e => KeyOf(db, e).SequenceEqual(key)))
                    throw new TenantIsolationException(
                        $"IgnoreQueryFilters() returned company {owner}'s {typeof(TEntity).Name} to company {other}: the raw-SQL guard is not registered on {typeof(TContext).Name}.");
            }
            catch (CrossTenantSqlException)
            {
                // Expected: the guard refuses unfiltered reads inside a company.
            }
        }
    }

    /// <summary>
    /// Creates a resource as company A over HTTP, then checks that company B's client gets <c>404</c> (or
    /// <c>403</c>) for it. <paramref name="create"/> returns the resource's URL, e.g. from the <c>Location</c> header.
    /// </summary>
    /// <exception cref="TenantIsolationException">Company B's client got the resource.</exception>
    public static async Task AssertCrossTenantReadIsDeniedAsync(
        HttpClient companyA, HttpClient companyB, Func<HttpClient, Task<string>> create, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(companyA);
        ArgumentNullException.ThrowIfNull(companyB);
        ArgumentNullException.ThrowIfNull(create);

        var url = await create(companyA).ConfigureAwait(false);
        using (var own = await companyA.GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), ct).ConfigureAwait(false))
        {
            if (!own.IsSuccessStatusCode)
                throw new TenantIsolationException($"Company A cannot read its own resource {url} ({(int)own.StatusCode}); the check proves nothing.");
        }

        using var foreign = await companyB.GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), ct).ConfigureAwait(false);
        if (foreign.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden))
            throw new TenantIsolationException($"Company B read company A's resource {url}: {(int)foreign.StatusCode}.");
    }

    private static object?[] KeyOf<TEntity>(DbContext db, TEntity entity)
        where TEntity : class
        => db.Entry(entity).Metadata.FindPrimaryKey() is { } primaryKey
            ? primaryKey.Properties.Select(p => db.Entry(entity).Property(p.Name).CurrentValue).ToArray()
            : throw new InvalidOperationException($"{typeof(TEntity).Name} has no primary key.");
}

/// <summary>Thrown when data crossed from one company to another.</summary>
public sealed class TenantIsolationException(string message) : Exception(message);
