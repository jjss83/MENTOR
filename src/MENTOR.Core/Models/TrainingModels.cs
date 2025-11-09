namespace MENTOR.Core.Models;

/// <summary>
/// Request model for starting a new training session.
/// </summary>
public record TrainingRequest(
    string ConfigPath,
    string RunId,
    string? ExecutablePath = null,
    int MaxSteps = 500_000,
    Dictionary<string, string>? Metadata = null
);

/// <summary>
/// Result model containing training session information.
/// </summary>
public record TrainingResult(
    Guid SessionId,
    string RunId,
    TrainingStatus Status,
    DateTime CreatedAt
);

/// <summary>
/// Represents a running training process.
/// </summary>
public record TrainingProcess(
    Guid SessionId,
    int ProcessId
);

/// <summary>
/// Generic result wrapper for operations that can succeed or fail.
/// </summary>
/// <typeparam name="T">Type of the value on success</typeparam>
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
