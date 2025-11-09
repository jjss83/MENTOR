namespace MENTOR.Core.Models;

/// <summary>
/// Configuration options for ML-Agents integration.
/// </summary>
public class MLAgentsOptions
{
    public const string SectionName = "MLAgents";

    /// <summary>
    /// Path to Python executable or command.
    /// </summary>
    public string PythonPath { get; set; } = "python";

    /// <summary>
    /// Default timeout for training sessions in seconds.
    /// </summary>
    public int DefaultTimeout { get; set; } = 3600;

    /// <summary>
    /// Directory where training results are stored.
    /// </summary>
    public string ResultsDirectory { get; set; } = "C:/MLAgents/results";

    /// <summary>
    /// Maximum number of concurrent training sessions.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 3;

    /// <summary>
    /// Additional command-line arguments to pass to mlagents-learn.
    /// </summary>
    public List<string> AdditionalArguments { get; set; } = new();
}

/// <summary>
/// Configuration options for database connection.
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// LiteDB connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "Filename=mentor.db;Mode=Exclusive";
}
