# Repository Guidelines

## Project Structure & Module Organization
`MentorMlApi/` is the active project: a .NET 9 minimal API that shells into Unity ML-Agents. Keep configuration POCOs under `Options/`, transport models under `Models/`, and process orchestration inside `Services/` so responsibilities stay isolated. Shared settings belong in `appsettings.json` plus `appsettings.Development.json` for local overrides; never hard-code machine paths elsewhere. Add automated tests as a sibling `MentorMlApi.Tests/` project so they can reuse the same solution and dependency graph.

## Build, Test, and Development Commands
Run `dotnet restore` after cloning, `dotnet build` before every PR, and `dotnet run` or `dotnet watch run` while iterating. Execute `dotnet test MentorMlApi.sln` once unit tests exist, using filters such as `--filter "Category=Process"` for focused runs. Exercise the HTTP surface with the cURL examples from `MentorMlApi/README.md`, and ensure the Unity ML-Agents repo lives at `X:\workspace\ml-agents` (or update `appsettings.*`) so relative trainer paths resolve.

## Coding Style & Naming Conventions
Stick to the default .NET formatter: 4-space indentation, file-scoped namespaces, and one public type per file. Nullable reference types are enabled in `MentorMlApi.csproj`, so treat warnings as failures. Use PascalCase for classes and DTOs, camelCase for locals, and retain the `I` prefix on interfaces (`IMlAgentsRunner`, `IMlAgentsProcessTracker`). When adding CLI switches, update both the model/property name and Swagger description so the generated OpenAPI stays accurate.

## Testing Guidelines
Favor xUnit for new tests, naming files `<TypeUnderTest>Tests.cs` and methods `Method_Scenario_Expectation`. Mock `IMlAgentsRunner` or `IMlAgentsProcessTracker` to avoid actually launching ML jobs, and assert on the constructed `mlagents-learn` command plus cancellation/reporting behavior. For integration smoke tests, point the API at a disposable ML-Agents checkout and script the `/mlagents/run` and `/mlagents/processes` calls with `curl`, capturing output in `logs/<runId>.txt`.

## Commit & Pull Request Guidelines
Follow the Conventional Commit pattern already in history (`feat: ...`, `fix: ...`, `chore: ...`) with imperative summaries. Squash unrelated changes, document config impacts, and include sample responses or screenshots when touching HTTP contracts. Every PR description should cite the linked issue (if any), list verification commands, and call out required updates to `appsettings.*`.

## Security & Configuration Tips
Store machine-specific paths, Conda executables, and Unity license information only in `appsettings.Development.json` or user-secrets. Double-check `UseCondaRun`, `CondaExecutable`, and `CondaEnvironmentName` values before reviewers pull your branch so they can reproduce the run command safely. When introducing new configuration keys, document them in `MentorMlApi/README.md` and supply defaults that do not trigger accidental long-running processes.
