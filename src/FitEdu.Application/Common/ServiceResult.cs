using System.Diagnostics.CodeAnalysis;

namespace FitEdu.Application.Common;

/// <summary>
/// Thin wrapper that carries a value together with the HTTP status the controller should return.
/// Lets service methods signal not-found, forbidden, etc. without throwing exceptions.
/// </summary>
// CA1000: the Ok/NotFound/... factories are the established way to build a result across the whole solution.
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory methods on the result type are used throughout the solution.")]
public record ServiceResult<T>(T? Value, int StatusCode = 200, string? ErrorMessage = null)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public static ServiceResult<T> Ok(T value) => new(value);
    public static ServiceResult<T> Created(T value) => new(value, 201);
    public static ServiceResult<T> NotFound(T? value = default, string? error = null) => new(value, 404, error);
    public static ServiceResult<T> Forbidden(T? value = default, string? error = null) => new(value, 403, error);
    public static ServiceResult<T> BadRequest(T? value = default, string? error = null) => new(value, 400, error);
    public static ServiceResult<T> Unauthorized(T? value = default, string? error = null) => new(value, 401, error);
    public static ServiceResult<T> Conflict(T? value = default, string? error = null) => new(value, 409, error);
    public static ServiceResult<T> NoContent() => new(default, 204);
}
