namespace TM.Web.NovelAgentWeb.Models.Common;

/// <summary>
/// Generic paged response wrapper for API endpoints with pagination.
/// </summary>
/// <typeparam name="T">The type of items in the page</typeparam>
public class PagedResponse<T>
{
    /// <summary>
    /// The items in the current page.
    /// </summary>
    public required List<T> Items { get; set; }

    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public required int TotalCount { get; set; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public required int PageNumber { get; set; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public required int PageSize { get; set; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;
}
