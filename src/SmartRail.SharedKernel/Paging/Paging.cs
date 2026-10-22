namespace SmartRail.SharedKernel.Paging;

/// <summary>
/// A page request. <see cref="Page"/> is one-based; <see cref="PageSize"/> is clamped to
/// <see cref="MaxPageSize"/> so a caller cannot ask for an unbounded result.
/// </summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public PageRequest(int page = 1, int pageSize = DefaultPageSize)
    {
        Page = page < 1 ? 1 : page;
        PageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };
    }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>How many records to skip for this page.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>
/// One page of results plus the totals a client needs to page through the rest.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;
}
