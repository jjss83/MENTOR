# Repository Guidelines

## Project Structure & Module Organization
The runner lives in the repo root: `Program.cs` bootstraps the CLI, `TrainingOptions.cs` validates flags, and `TrainingSessionRunner.cs` builds and monitors the `mlagents-learn` process. Build output flows into `bin/` and `obj/`; keep generated ML-Agents artifacts in `X:/workspace/ml-agents/results` (or any directory you pass with `--results-dir`) so Git stays tidy. Add future components under scoped folders such as `src/` for runtime code and `tests/` for automation to keep responsibilities clear.

## Build, Test, and Development Commands
- `dotnet restore` — downloads package references after a fresh clone or branch switch.
- `dotnet build [-c Release]` — compiles the console app; use `-c Release` for artifacts you plan to ship.
- `dotnet run -- --env-path "X:/path/Env.exe" --config "X:/path/trainer.yaml" [options]` — launches an end-to-end training session (append `--skip-conda` if the environment is already active).
- `dotnet publish -c Release -o dist` — produces a redistributable folder you can copy to a training machine.

## Coding Style & Naming Conventions
Stick to file-scoped namespaces, 4-space indentation, and analyzer-friendly usage of `var` (only when the type is obvious). Follow .NET casing rules: `PascalCase` for types/methods, `camelCase` for locals, and ALL_CAPS for constant identifiers. Keep validation inside `TrainingOptions` and side effects (process, filesystem, console) inside `TrainingSessionRunner`. Run `dotnet format` before committing to keep spacing, imports, and line endings consistent.

## Testing Guidelines
Tests are not yet committed; create an xUnit project under `tests/MentorTrainingRunner.Tests`. Name test classes `<TypeUnderTest>Tests` and methods `Method_Scenario_ExpectedResult`. Focus on argument parsing edge cases, graceful cancellation, and command construction using process wrappers so you do not spawn real Unity jobs. Execute `dotnet test` locally and attach its output (or CI link) to pull requests.

## Commit & Pull Request Guidelines
Commits follow Conventional Commits (`feat: ...`, `fix: ...`, `chore: ...`). Keep subjects under ~72 characters and describe behavior changes, not file lists. PR descriptions should include: problem statement, summary of changes, verification steps (build/test/run commands), and any relevant logs or TensorBoard screenshots. Link tracking issues and call out operational risks such as run duration or required infrastructure.

## Security & Configuration Tips
Store Unity environment executables, trainer configs, and Conda environments outside the repo; reference them with absolute or workspace-relative paths at runtime. Never commit Conda credentials, TensorBoard artifacts, or proprietary environment builds. Validate file paths before launching runs to avoid executing untrusted binaries, and prefer read-only storage mounts when copying results off shared lab machines.
