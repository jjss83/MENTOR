using System;
using System.Globalization;
using System.IO;
using FluentAssertions;
using MentorTrainingRunner;

public class TrainingOptionsTests
{
    [Fact]
    public void TryParse_GeneratesRunIdAndDefaults()
    {
        var originalDefault = TrainingOptions.DefaultResultsDirectory;
        var tempDir = CreateTempDirectory();
        var configPath = WriteTrainerConfig(tempDir, "alpha-beta-gamma");
        TrainingOptions.SetDefaultResultsDirectory(tempDir);

        try
        {
            var parsed = TrainingOptions.TryParse(new[] { "--config", configPath }, out var options, out var error);

            parsed.Should().BeTrue();
            options.Should().NotBeNull();
            error.Should().BeNull();

            var today = DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);
            options!.EnvExecutablePath.Should().BeNull();
            options.TrainerConfigPath.Should().Be(Path.GetFullPath(configPath));
            options.ResultsDirectory.Should().Be(Path.GetFullPath(tempDir));
            options.CondaEnvironmentName.Should().Be("mlagents");
            options.BasePort.Should().BeNull();
            options.NoGraphics.Should().BeFalse();
            options.SkipConda.Should().BeFalse();
            options.LaunchTensorBoard.Should().BeFalse();
            options.Resume.Should().BeFalse();
            options.RunId.Should().StartWith($"abg-{today}-");
        }
        finally
        {
            TrainingOptions.SetDefaultResultsDirectory(originalDefault);
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TryParse_IncrementsSequenceBasedOnExistingRuns()
    {
        var originalDefault = TrainingOptions.DefaultResultsDirectory;
        var tempDir = CreateTempDirectory();
        var configPath = WriteTrainerConfig(tempDir, "alpha-beta-gamma");
        TrainingOptions.SetDefaultResultsDirectory(tempDir);

        try
        {
            TrainingOptions.TryParse(new[] { "--config", configPath }, out var first, out _).Should().BeTrue();
            var firstRunId = first!.RunId;
            Directory.CreateDirectory(Path.Combine(tempDir, firstRunId));

            var prefixWithoutSequence = firstRunId[..firstRunId.LastIndexOf('-')];
            var secondRunId = $"{prefixWithoutSequence}-2";
            Directory.CreateDirectory(Path.Combine(tempDir, secondRunId));

            TrainingOptions.TryParse(new[] { "--config", configPath }, out var options, out var error).Should().BeTrue();

            options!.RunId.Should().EndWith("-3");
            options.RunId.Should().StartWith(prefixWithoutSequence);
            error.Should().BeNull();
        }
        finally
        {
            TrainingOptions.SetDefaultResultsDirectory(originalDefault);
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TryParse_FailsWhenConfigDoesNotExist()
    {
        var missingConfig = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.yaml");

        var parsed = TrainingOptions.TryParse(new[] { "--config", missingConfig }, out var options, out var error);

        parsed.Should().BeFalse();
        options.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mentor-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteTrainerConfig(string directory, string behaviorName)
    {
        var path = Path.Combine(directory, "trainer.yaml");
        File.WriteAllText(path, $"behaviors:\n  {behaviorName}:\n    trainer_type: ppo\n");
        return path;
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
