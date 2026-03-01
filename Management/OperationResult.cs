namespace WebNet.CatalogServer;

public readonly record struct OperationResult<T>(bool IsSuccess, T? Value, string ErrorCode, string ErrorMessage)
{
    public static OperationResult<T> Ok(T value) => new(true, value, string.Empty, string.Empty);

    public static OperationResult<T> Fail(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}

public sealed record OperationError(string ErrorCode, string ErrorMessage);
