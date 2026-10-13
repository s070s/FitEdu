namespace FitEdu.Application.Common;

/// <summary>
/// Shared page/pageSize normalization for every list endpoint and service. Clamp-to-max
/// semantics: an oversized pageSize is capped at <see cref="MaxPageSize"/> and an oversized page
/// at <see cref="MaxPage"/>, not silently reset to some smaller default, and page/pageSize are
/// never allowed below 1.
/// </summary>
public static class PagingParams
{
    public const int MaxPageSize = 100;

    // Keeps (page - 1) * pageSize, the row count a service skips, far below int.MaxValue.
    public const int MaxPage = 10_000;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Clamp(page, 1, MaxPage), Math.Clamp(pageSize, 1, MaxPageSize));
}
