namespace ProcureFlow.Shared.Presentation.Helpers;

/// <summary>
/// Pagination validation and default settings for API endpoints.
/// Prevents resource exhaustion attacks (DoS) by enforcing maximum page sizes.
/// </summary>
public static class PaginationHelper
{
    /// <summary>Default page size if not specified (10 items)</summary>
    public const int DefaultPageSize = 10;

    /// <summary>Maximum allowed page size to prevent DoS attacks (100 items max)</summary>
    public const int MaxPageSize = 100;

    /// <summary>Maximum allowed skip value (prevents unbounded offset attacks)</summary>
    public const int MaxSkipValue = 10000;

    /// <summary>
    /// Validates and normalizes pagination parameters.
    /// 
    /// Rules:
    /// - PageSize defaults to 10, max 100
    /// - Skip defaults to 0, max 10000
    /// - PageNumber defaults to 1, max 1000
    /// 
    /// Returns normalized values safe for database queries.
    /// </summary>
    /// <param name="pageSize">Requested page size (max 100)</param>
    /// <param name="skip">Requested skip value (max 10000)</param>
    /// <returns>Validated (pageSize, skip) tuple</returns>
    public static (int PageSize, int Skip) ValidateAndNormalize(int? pageSize, int? skip)
    {
        // Validate and normalize page size
        int validPageSize = pageSize ?? DefaultPageSize;
        if (validPageSize <= 0)
        {
            validPageSize = DefaultPageSize;
        }

        if (validPageSize > MaxPageSize)
        {
            validPageSize = MaxPageSize;
        }

        // Validate and normalize skip value
        int validSkip = skip ?? 0;
        if (validSkip < 0)
        {
            validSkip = 0;
        }

        if (validSkip > MaxSkipValue)
        {
            validSkip = MaxSkipValue;
        }

        return (validPageSize, validSkip);
    }

    /// <summary>Maximum allowed page number to prevent deep pagination attacks</summary>
    public const int MaxPageNumber = 1000;

    /// <summary>
    /// Validates and normalizes page number-based pagination.
    ///
    /// Rules:
    /// - PageNumber defaults to 1, max 1000
    /// - PageSize defaults to 10, max 100
    /// - Skip is derived as (pageNumber - 1) * pageSize, max 10000
    ///
    /// Returns all three normalized values so callers don't need to duplicate logic.
    /// </summary>
    /// <param name="pageNumber">Requested page number (1-based)</param>
    /// <param name="pageSize">Requested page size (max 100)</param>
    /// <returns>Validated (PageNumber, PageSize, Skip) tuple</returns>
    public static (int PageNumber, int PageSize, int Skip) ValidateAndNormalizePageNumber(int? pageNumber, int? pageSize)
    {
        int validPageNumber = pageNumber ?? 1;
        if (validPageNumber <= 0)
        {
            validPageNumber = 1;
        }

        if (validPageNumber > MaxPageNumber)
        {
            validPageNumber = MaxPageNumber;
        }

        int validPageSize = pageSize ?? DefaultPageSize;
        if (validPageSize <= 0)
        {
            validPageSize = DefaultPageSize;
        }

        if (validPageSize > MaxPageSize)
        {
            validPageSize = MaxPageSize;
        }

        int skip = (validPageNumber - 1) * validPageSize;
        if (skip > MaxSkipValue)
        {
            skip = MaxSkipValue;
        }

        return (validPageNumber, validPageSize, skip);
    }

    /// <summary>
    /// Gets validation error message for client if pagination is invalid.
    /// </summary>
    /// <param name="pageSize">Requested page size</param>
    /// <param name="skip">Requested skip value</param>
    /// <returns>Error message, or null if valid</returns>
    public static string? GetValidationError(int? pageSize, int? skip)
    {
        if (pageSize.HasValue && pageSize <= 0)
        {
            return "PageSize must be greater than 0.";
        }

        if (pageSize.HasValue && pageSize > MaxPageSize)
        {
            return $"PageSize cannot exceed {MaxPageSize}. Requested: {pageSize}.";
        }

        if (skip.HasValue && skip < 0)
        {
            return "Skip cannot be negative.";
        }

        if (skip.HasValue && skip > MaxSkipValue)
        {
            return $"Skip cannot exceed {MaxSkipValue} (maximum {MaxSkipValue / MaxPageSize} pages). Requested: {skip}.";
        }

        return null;
    }
}

/// <summary>
/// Standard pagination request model for API endpoints.
/// Enforces maximum limits for security.
/// </summary>
public sealed record PaginationRequest
{
    /// <summary>Page size (max 100)</summary>
    public int? PageSize { get; init; }

    /// <summary>Number of items to skip (max 10000)</summary>
    public int? Skip { get; init; }

    /// <summary>Validate pagination parameters</summary>
    /// <returns>Validation error message, or null if valid</returns>
    public string? Validate()
    {
        return PaginationHelper.GetValidationError(PageSize, Skip);
    }

    /// <summary>Get normalized pagination parameters for database query</summary>
    /// <returns>(PageSize, Skip) tuple safe for database</returns>
    public (int PageSize, int Skip) Normalize()
    {
        return PaginationHelper.ValidateAndNormalize(PageSize, Skip);
    }
}

/// <summary>
/// Standard pagination response wrapper for API responses.
/// </summary>
/// <typeparam name="T">Item type in the page</typeparam>
public sealed record PaginatedResponse<T>
{
    /// <summary>Items in this page</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total count of all items matching filter (not just this page)</summary>
    public required int TotalCount { get; init; }

    /// <summary>Current page size</summary>
    public required int PageSize { get; init; }

    /// <summary>Number of items skipped</summary>
    public required int Skip { get; init; }

    /// <summary>Total pages available</summary>
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;

    /// <summary>Whether there are more pages after this one</summary>
    public bool HasNextPage => Skip + PageSize < TotalCount;

    /// <summary>Whether there are pages before this one</summary>
    public bool HasPreviousPage => Skip > 0;
}
