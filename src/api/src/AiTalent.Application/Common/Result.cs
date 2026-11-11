namespace AiTalent.Application.Common;

public enum ErrorKind
{
    Invalid,
    NotFound,
    Forbidden,
    Conflict,
    TooManyRequests,
    Unavailable,
}

public sealed record AppError(ErrorKind Kind, string Message);

public sealed class Result<T>
{
    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public AppError? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(AppError error) => new(default, error);

    public static Result<T> Failure(ErrorKind kind, string message) => new(default, new AppError(kind, message));
}
