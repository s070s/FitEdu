namespace FitEdu.Web.Consumers;

/// <summary>
/// The answer to "fetch one item by its key" that tells the three outcomes apart: found
/// (<see cref="Item"/> set), not found (a 404), or a failed request (any other error status, a
/// network failure or a timeout). A failed request says nothing about whether the item exists.
/// </summary>
public sealed record ApiLookup<T>(T? Item, bool RequestFailed) where T : class
{
    public static ApiLookup<T> NotFound { get; } = new(null, RequestFailed: false);

    public static ApiLookup<T> Failure { get; } = new(null, RequestFailed: true);

    public static ApiLookup<T> Found(T item) => new(item, RequestFailed: false);
}
