using System;
using System.Collections.Generic;

namespace Contracts.Common;

/// <summary>
/// Represents the result of a paginated query.
/// </summary>
public sealed class PageResult<T>
{
    public PageResult()
        : this(Array.Empty<T>(), 0)
    {
    }

    public PageResult(IReadOnlyList<T> items, int totalCount)
    {
        Items = items ?? Array.Empty<T>();
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; init; }

    public int TotalCount { get; init; }
}
