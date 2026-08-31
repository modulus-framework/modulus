namespace Modulus.Core.Abstractions;

/// <summary>
/// Marker interface for entities that opt into optimistic concurrency
/// protection via EF Core's concurrency-token mechanism. Apply it to any
/// entity that carries a <c>string ConcurrencyStamp { get; set; }</c> property.
/// <para>
/// <c>ModuleDbContext</c> does two things for such entities: it marks the
/// property as an EF concurrency token, and it rotates the stamp on every
/// insert and update during <c>SaveChangesAsync</c>. The rotation is the
/// framework's job, not EF's — EF self-populates only store-generated tokens
/// such as SQL Server <c>rowversion</c>, so a never-rotated string token would
/// make every concurrency predicate match and silently permit lost updates.
/// </para>
/// <para>
/// The result is a <c>DbUpdateConcurrencyException</c> when a concurrent writer
/// changed the row since it was read, which the framework's
/// <c>GlobalExceptionHandler</c> translates to <c>409 Conflict</c>.
/// </para>
/// </summary>
public interface IHasConcurrencyStamp
{
    /// <summary>
    /// Opaque string stamped by the framework on every write. Application code
    /// never assigns this — but a disconnected client <i>does</i> round-trip the
    /// value it read, and that returned value becomes the concurrency predicate
    /// when the entity is re-attached as modified.
    /// </summary>
    string ConcurrencyStamp { get; set; }
}
