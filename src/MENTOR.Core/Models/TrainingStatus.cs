namespace MENTOR.Core.Models;

/// <summary>
/// Represents the current status of a training session.
/// </summary>
public enum TrainingStatus
{
    /// <summary>
    /// Training session is queued but not yet started.
    /// </summary>
    Queued,

    /// <summary>
    /// Training session is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// Training session completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Training session failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Training session was cancelled by user.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Training session is stopping.
    /// </summary>
    Stopping
}
