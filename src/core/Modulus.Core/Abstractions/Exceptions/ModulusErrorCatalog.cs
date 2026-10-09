namespace Modulus.Core.Abstractions.Exceptions;

/// <summary>The protocol-neutral class of a failure; each surface (HTTP, gRPC, GraphQL) maps it to its own status.</summary>
public enum ModulusErrorKind
{
    Validation,
    NotFound,
    Unauthenticated,
    PermissionDenied,
    Conflict,
    ConcurrencyConflict,
    FeatureDisabled,
    Cancelled,
    Internal,
}

/// <summary>A classified failure: the kind, the stable machine-readable code and whether the caller caused it.</summary>
/// <param name="Kind">What went wrong, independent of the protocol.</param>
/// <param name="Code">The stable code shared by every surface (gRPC <c>ErrorInfo.reason</c>, GraphQL <c>extensions.code</c>, problem <c>code</c>).</param>
/// <param name="Title">The client-safe message (never exception text).</param>
/// <param name="IsClientError">True for a 4xx-class failure; false for a server fault.</param>
public sealed record ModulusError(ModulusErrorKind Kind, string Code, string Title, bool IsClientError);

/// <summary>
/// The one exception-to-error table the HTTP exception handler, the gRPC interceptor and the GraphQL error provider all
/// read, so a new exception type is mapped once and the surfaces cannot drift apart.
/// </summary>
public static class ModulusErrorCatalog
{
    private static readonly ModulusError s_validation = new(ModulusErrorKind.Validation, "VALIDATION_FAILED", "Validation failed", true);
    private static readonly ModulusError s_notFound = new(ModulusErrorKind.NotFound, "NOT_FOUND", "Resource not found", true);
    private static readonly ModulusError s_unauthenticated = new(ModulusErrorKind.Unauthenticated, "UNAUTHENTICATED", "Unauthorized", true);
    private static readonly ModulusError s_permissionDenied = new(ModulusErrorKind.PermissionDenied, "PERMISSION_DENIED", "Forbidden", true);
    private static readonly ModulusError s_conflict = new(ModulusErrorKind.Conflict, "CONFLICT", "Conflict", true);
    private static readonly ModulusError s_concurrency = new(ModulusErrorKind.ConcurrencyConflict, "CONCURRENCY_CONFLICT", "Concurrent update conflict", true);
    private static readonly ModulusError s_featureDisabled = new(ModulusErrorKind.FeatureDisabled, "FEATURE_DISABLED", "Feature not available", true);
    private static readonly ModulusError s_cancelled = new(ModulusErrorKind.Cancelled, "CANCELLED", "The request was cancelled", true);
    private static readonly ModulusError s_internal = new(ModulusErrorKind.Internal, "INTERNAL", "An unexpected error occurred", false);

    /// <summary>Classifies <paramref name="exception"/>; anything unknown is <see cref="ModulusErrorKind.Internal"/>.</summary>
    public static ModulusError Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ValidationException => s_validation,
            NotFoundException => s_notFound,
            UnauthorizedException => s_unauthenticated,
            ForbiddenException => s_permissionDenied,
            CrossTenantWriteException => s_permissionDenied,
            ConflictException => s_conflict,
            FeatureDisabledException => s_featureDisabled,
            OperationCanceledException => s_cancelled,
            _ when IsDbUpdateConcurrencyException(exception) => s_concurrency,
            _ => s_internal,
        };
    }

    // Matches EF Core's DbUpdateConcurrencyException without referencing EF Core.
    private static bool IsDbUpdateConcurrencyException(Exception exception)
        => string.Equals(exception.GetType().FullName, "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException", StringComparison.Ordinal);
}
