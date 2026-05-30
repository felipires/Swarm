namespace Swarm.Cluster.Models.Dto;

/// <summary>
/// Offset-paginated list response (roadmap P3-1). <see cref="Total"/> is the
/// full unfiltered count so the caller can compute total pages. Cursor-based
/// pagination for high-frequency endpoints (instances, logs) is a follow-up.
/// </summary>
public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

/// <summary>
/// Query-string-bound pagination parameters. <c>page</c> is 1-indexed.
/// </summary>
public class PageRequest
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize
    {
        get
        {
            if (PageSize <= 0) return DefaultPageSize;
            if (PageSize > MaxPageSize) return MaxPageSize;
            return PageSize;
        }
    }

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
