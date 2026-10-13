namespace FitEdu.Web.Consumers;

/// <summary>
/// The answer to "fetch one item by its key" that tells the three outcomes apart: found
/// (<see cref="Item"/> set), not found (a 404), or a failed request (any other error status, a
/// network failure or a timeout). A failed request says nothing about whether the item exists.
/// Build one with <see cref="ApiLookup"/>.
/// </summary>
public sealed record ApiLookup<T>(T? Item, bool RequestFailed) where T : class;

/// <summary>Creates <see cref="ApiLookup{T}"/> results.</summary>
public static class ApiLookup
{
    public static ApiLookup<T> Found<T>(T item) where T : class => new(item, RequestFailed: false);

    public static ApiLookup<T> NotFound<T>() where T : class => new(null, RequestFailed: false);

    public static ApiLookup<T> Failure<T>() where T : class => new(null, RequestFailed: true);
}
