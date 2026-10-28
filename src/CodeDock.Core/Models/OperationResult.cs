namespace CodeDock.Core.Models;

public sealed record OperationResult(bool IsSuccess, string? Error = null)
{
    public static OperationResult Success() => new(true);

    public static OperationResult Failure(string error) => new(false, error);
}

public sealed record OperationResult<T>(bool IsSuccess, T? Value = default, string? Error = null)
{
    public static OperationResult<T> Success(T value) => new(true, value);

    public static OperationResult<T> Failure(string error) => new(false, default, error);
}
