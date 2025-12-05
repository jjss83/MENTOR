using System.Collections.Generic;
using FluentAssertions;

public class CliArgsTests
{
    [Fact]
    public void FromTraining_BuildsArgumentsInExpectedOrder()
    {
        var request = new TrainingRequest(
            ResultsDir: "X:/results",
            CondaEnv: "mlagents-dev",
            BasePort: 6000,
            NoGraphics: true,
            SkipConda: true,
            Tensorboard: true,
            Resume: true,
            EnvPath: "env.exe",
            Config: "config.yaml",
            RunId: "ignored"
        );

        var args = CliArgs.FromTraining(
            request,
            envPathOverride: "override-env.exe",
            configOverride: "override-config.yaml",
            runIdOverride: "run-123"
        );

        args.Should().Equal(new[]
        {
            "--env-path", "override-env.exe",
            "--config", "override-config.yaml",
            "--run-id", "run-123",
            "--results-dir", "X:/results",
            "--conda-env", "mlagents-dev",
            "--base-port", "6000",
            "--no-graphics",
            "--skip-conda",
            "--tensorboard",
            "--resume"
        });
    }

    [Fact]
    public void FromTraining_SkipsMissingAndFalseyValues()
    {
        var request = new TrainingRequest(
            ResultsDir: null,
            CondaEnv: null,
            BasePort: null,
            NoGraphics: null,
            SkipConda: null,
            Tensorboard: null,
            Resume: null,
            EnvPath: null,
            Config: null,
            RunId: null
        );

        var args = CliArgs.FromTraining(request);

        args.Should().BeEmpty();
    }
}
