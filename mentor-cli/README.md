# Mentor Training Runner

A lightweight .NET console utility that wraps `mlagents-learn`, ensures the proper Conda environment is used, and streams logs directly to your terminal so you can launch Unity ML-Agents training sessions from scripted workflows.

## Highlights
- Validates environment and trainer config paths before shelling out so you catch mistakes early.
- Keeps artifacts in `X:\workspace\ml-agents\results` (or your `--results-dir`) so Unity output stays out of the repo.
- Supports Conda and PATH-based installs, optional TensorBoard bootstrapping, and graceful cancellation with `Ctrl+C`.

## Prerequisites
- .NET SDK 9.0 or later
- Unity ML-Agents installed in a Conda env named `mlagents` (or adjust `--conda-env` / `--skip-conda`)
- A compiled Unity ML-Agents environment executable (`.exe`) and trainer YAML config
- TensorBoard available on PATH or inside the same Conda env if you plan to pass `--tensorboard`

> The runner does **not** build your Unity environment. Follow the [Unity ML-Agents docs](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Sample.html) to produce an executable first.

## Quick Start
```bash
dotnet restore
dotnet build
dotnet run -- \
  --env-path "X:\workspace\ml-agents\Builds\MyEnv\MyEnv.exe" \
  --config "X:\workspace\ml-agents\config\ppo_trainer.yaml" \
  --run-id mentor-demo \
  --no-graphics \
  --tensorboard
```
The CLI prints the resolved `mlagents-learn` command, streams stdout/stderr (including color), and wires TensorBoard output directly to the console.

## CLI Reference
```bash
dotnet run -- --env-path <path-to-env-exe> --config <trainer-config.yaml> [options]
```
| Option | Required | Description |
| --- | --- | --- |
| `--env-path <path>` | Yes | Absolute or relative path to the Unity environment executable. Validated before launch. |
| `--config <path>` | Yes | Trainer configuration YAML passed directly to `mlagents-learn`. |
| `--run-id <id>` | No | Friendly name for the session. Defaults to `run_<UTC timestamp>`. |
| `--results-dir <path>` | No | Storage for TensorBoard summaries and checkpoints. Default: `X:\workspace\ml-agents\results`. Directories are created automatically. |
| `--conda-env <name>` | No | Conda environment that contains ML-Agents (default `mlagents`). Combine with `--skip-conda` if the environment is already activated. |
| `--skip-conda` | No | Invoke `mlagents-learn`/`tensorboard` from PATH instead of `conda run`. |
| `--base-port <port>` | No | Override the base communication port when launching multiple instances. |
| `--no-graphics` | No | Adds the Unity `--no-graphics` flag to save GPU/CPU when rendering is unnecessary. |
| `--tensorboard` | No | Starts TensorBoard pointed at the resolved results directory and streams its logs. |

### Cancellation & Process Lifecycle
- Press `Ctrl+C` once for a graceful shutdown (both `mlagents-learn` and TensorBoard are asked to stop); press it again to force termination.
- All stdout/stderr is pumped to the console so you can pipe to log files or other tools.
- The resolved command line is displayed before execution to simplify debugging.

## Results & Monitoring
- Artifacts land in `--results-dir` under the `run-id` folder, matching the structure Unity expects for TensorBoard/TensorFlow checkpoints.
- Passing `--tensorboard` auto-launches TensorBoard via the same Conda env (or PATH) and surfaces the listening URL directly in your console.
- Keep long-running environment builds, Conda envs, and generated models outside the repo (for example, under `X:/workspace/ml-agents/`) to avoid accidental commits.

## Development & Deployment
- `dotnet build [-c Release]` compiles the CLI; use Release when preparing artifacts for other machines.
- `dotnet publish -c Release -o dist` produces a self-contained folder you can copy to a training workstation -- remember to bring your Unity env executable and trainer config.
- Before contributing changes, run `dotnet format` to satisfy analyzers and keep spacing tidy.
- Automated tests will live under `tests/MentorTrainingRunner.Tests` (xUnit). Focus scenarios: option parsing edge cases, graceful cancellation handling, and command construction without spawning real Unity jobs.

## Troubleshooting
- **Conda not found:** Ensure `CONDA_EXE` is set or `conda` is resolvable on PATH. Otherwise use `--skip-conda` within an activated environment.
- **File path errors:** The CLI validates `--env-path` and `--config`. Provide fully qualified paths if you see "Could not find" errors.
- **TensorBoard launch issues:** Confirm `tensorboard` is installed in the same Conda env or globally on PATH.

Happy training! Keep TensorBoard artifacts and proprietary Unity builds outside Git to protect sensitive IP.
