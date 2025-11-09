namespace MENTOR.Core.Models;

/// <summary>
/// Represents a ML-Agents training session with its metadata and state.
/// </summary>
public class TrainingSession
{
    /// <summary>
    /// Unique identifier for this training session.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// User-provided run identifier for the training session.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Path to the training configuration YAML file.
    /// </summary>
    public required string ConfigPath { get; init; }

    /// <summary>
    /// Optional path to the Unity executable (null for Editor mode).
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Maximum number of training steps.
    /// </summary>
    public int MaxSteps { get; init; }

    /// <summary>
    /// Current status of the training session.
    /// </summary>
    public TrainingStatus Status { get; set; }

    /// <summary>
    /// When the training session was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the training session started running.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the training session ended (completed, failed, or cancelled).
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Process ID of the mlagents-learn process.
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// Error message if the training failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Directory where training results are stored.
    /// </summary>
    public string? ResultsDirectory { get; set; }

    /// <summary>
    /// Additional metadata stored as key-value pairs.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
