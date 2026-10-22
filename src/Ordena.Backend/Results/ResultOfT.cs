namespace Ordena.Backend.Results;

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value) : base(null, null) => _value = value;

    internal Result(ErrorCode error, string? detail) : base(error, detail) { }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result failed with {Error}, so it has no value.");
}
