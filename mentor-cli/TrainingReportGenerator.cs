using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MentorTrainingRunner;

internal sealed class TrainingReportGenerator
{
    private readonly ReportOptions options;

    public TrainingReportGenerator(ReportOptions options)
    {
        this.options = options;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = await GenerateReportAsync(cancellationToken);
        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        Console.WriteLine(report.ToJsonString(serializerOptions));
        return 0;
    }

    public async Task<JsonObject> GenerateReportAsync(CancellationToken cancellationToken = default)
    {
        var runDirectory = Path.Combine(options.ResultsDirectory, options.RunId);
        if (!Directory.Exists(runDirectory))
        {
            throw new InvalidOperationException($"Run directory not found at '{runDirectory}'.");
        }

        var runLogsDirectory = Path.Combine(runDirectory, "run_logs");
        if (!Directory.Exists(runLogsDirectory))
        {
            throw new InvalidOperationException($"Run logs directory not found at '{runLogsDirectory}'.");
        }

        var trainingStatusPath = Path.Combine(runLogsDirectory, "training_status.json");
        if (!File.Exists(trainingStatusPath))
        {
            throw new InvalidOperationException(
                $"training_status.json not found at '{trainingStatusPath}'. Ensure the run completed successfully."
            );
        }

        var reportRoot = new JsonObject
        {
            ["runId"] = options.RunId,
            ["resultsDirectory"] = options.ResultsDirectory,
            ["runDirectory"] = runDirectory,
            ["runLogsDirectory"] = runLogsDirectory,
        };

        var trainingStatusContent = await LoadJsonAsync(trainingStatusPath, cancellationToken);
        var artifacts = new JsonObject
        {
            ["trainingStatus"] = BuildArtifact(trainingStatusPath, trainingStatusContent),
        };

        var timersPath = Path.Combine(runLogsDirectory, "timers.json");
        if (File.Exists(timersPath))
        {
            var timersContent = await LoadJsonAsync(timersPath, cancellationToken);
            artifacts["timers"] = BuildArtifact(timersPath, timersContent);
        }
        else
        {
            artifacts["timers"] = BuildMissingArtifact(timersPath);
        }

        var configurationPath = Path.Combine(runDirectory, "configuration.yaml");
        if (File.Exists(configurationPath))
        {
            var configurationText = await File.ReadAllTextAsync(configurationPath, cancellationToken);
            artifacts["configuration"] = BuildArtifact(configurationPath, JsonValue.Create(configurationText));
        }
        else
        {
            artifacts["configuration"] = BuildMissingArtifact(configurationPath);
        }

        reportRoot["artifacts"] = artifacts;
        return reportRoot;
    }

    private static JsonObject BuildArtifact(string path, JsonNode? content)
    {
        return new JsonObject
        {
            ["path"] = path,
            ["exists"] = true,
            ["content"] = content,
        };
    }

    private static JsonObject BuildMissingArtifact(string path)
    {
        return new JsonObject
        {
            ["path"] = path,
            ["exists"] = false,
            ["content"] = null,
        };
    }

    private static async Task<JsonNode> LoadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        if (node is null)
        {
            throw new InvalidOperationException($"The JSON file '{path}' was empty.");
        }

        return node;
    }
}
