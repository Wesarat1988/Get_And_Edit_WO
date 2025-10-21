using System;

namespace Contracts.Common;

/// <summary>
/// Represents a paginated work order search query.
/// </summary>
public sealed record PageRequest(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    string? Line,
    string? PartNo,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo);
