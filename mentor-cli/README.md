# Mentor Training Runner

A lightweight .NET console utility for launching Unity ML-Agents training runs against a pre-built learning environment executable. It wraps `mlagents-learn`, ensures the proper Conda environment is used, and stores results under `X:\workspace\ml-agents\results` by default.

## Prerequisites

- .NET SDK 9.0 or later
- Unity ML-Agents tools already installed in the `mlagents` Conda environment (or available on `PATH` if you intend to skip Conda)
- A compiled Unity ML-Agents environment executable and a trainer configuration `.yaml` file

> The project does **not** create or manage the ML-Agents environment—ensure you have already built it following the [Unity ML-Agents docs](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Sample.html) and [environment executable guide](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Learning-Environment-Executable.html).

## Build

```
dotnet build
```

## Usage

```
dotnet run -- \
  --env-path "X:\path\to\MyEnv.exe" \
  --config "X:\workspace\ml-agents\config\trainer_config.yaml" \
  [options]
```

### Options

| Option | Description |
| --- | --- |
| `--run-id <id>` | Optional identifier for the run. Defaults to `run_<UTC timestamp>`. |
| `--results-dir <path>` | Where to store TensorBoard summaries and model files. Default: `X:\workspace\ml-agents\results`. |
| `--conda-env <name>` | Conda environment that hosts ML-Agents. Default: `mlagents`. |
| `--base-port <port>` | Base communication port for the executable. Useful when running multiple instances. |
| `--no-graphics` | Launches the Unity environment without rendering. |
| `--skip-conda` | Run `mlagents-learn` directly from `PATH` instead of `conda run`. |
| `--tensorboard` | Launch TensorBoard alongside training, pointing it at the resolved results directory. |

The runner prints the resolved `mlagents-learn` command before launching and streams stdout/stderr to the console. Press `Ctrl+C` once to request a graceful shutdown; press again to force termination.

## Example

```
dotnet run -- \
  --env-path "X:\workspace\ml-agents\Builds\MyEnv\MyEnv.exe" \
  --config "X:\workspace\ml-agents\config\ppo_trainer.yaml" \
  --run-id mentor-demo \
  --base-port 5005 \
  --no-graphics
```

The runner now lets `mlagents-learn` write straight to whatever stdout/stderr Mentors's console is using, so you’ll see the native logs (including colorized progress output) both interactively and when piping to other tools. If you have already activated the ML-Agents environment (e.g., via `conda activate mlagents`), add `--skip-conda` to avoid launching a nested Conda session.
