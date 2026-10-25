using System.Diagnostics.CodeAnalysis;

namespace Ordena.Backend.Results;

// The outcome of a service call: success, or an expected failure with a code.
// Real bugs are exceptions, never a failed Result.
public class Result
{
    protected Result(ErrorCode? error, string? detail)
    {
        Error = error;
        Detail = detail;
    }

    public ErrorCode? Error { get; }
    public string? Detail { get; }

    // After a failed check the compiler knows Error is set, so callers can pass it on.
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Ok() => new(null, null);
    public static Result Fail(ErrorCode error, string? detail = null) => new(error, detail);

    public static Result<T> Ok<T>(T value) => new(value);
    public static Result<T> Fail<T>(ErrorCode error, string? detail = null) => new(error, detail);

    // This failure passed on as another result type, with its code and detail.
    public Result<T> FailAs<T>() => IsSuccess
        ? throw new InvalidOperationException("Only a failed result can be passed on.")
        : new(Error.Value, Detail);
}
