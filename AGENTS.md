# Repository Guidelines

## Project Structure & Module Organization
The solution currently centers on `MentorMlApi/`, an ASP.NET Core 9 minimal API. Source is grouped by responsibility: configuration models live under `Options/`, request/response contracts live in `Models/`, and orchestrators for external processes sit in `Services/`. Shared configuration such as `appsettings.json` and its development override stay at the project root; keep machine-specific paths in `appsettings.Development.json` or user-secrets so they do not leak into commits. Place future automated tests beside the API in a sibling `MentorMlApi.Tests/` project so they can share fixtures.

## Build, Test, and Development Commands
From `MentorMlApi/`, run `dotnet restore` once per clone, `dotnet build` for CI-style validation, and `dotnet run` (or `dotnet watch run`) for local debugging. When test projects exist, run `dotnet test MentorMlApi.sln` to execute everything; prefer filtered runs such as `dotnet test --filter "Category=Process"` for targeted suites. Use the sample cURL snippet in `README.md` to exercise `/mlagents/run` and `/mlagents/processes` endpoints; keep the Unity ML-Agents repo at `X:\workspace\ml-agents` so relative paths resolve correctly.

## Coding Style & Naming Conventions
Follow the default .NET formatting: 4-space indentation, file-scoped namespaces when possible, and one public type per file. Keep nullable reference types enabled (see `MentorMlApi.csproj`) and treat warnings as actionable. Respect existing naming: PascalCase for classes, camelCase for locals, and `I` prefixes for interfaces (`IMlAgentsRunner`). When adding CLI switches or DTO fields, mirror the `mlagents-learn` flag names and update both the model and service so Swagger stays accurate.

## Testing Guidelines
Aim for xUnit-based tests stored in `MentorMlApi.Tests/`. Name files `<TypeUnderTest>Tests.cs` and methods `Method_Scenario_Expectation`. Mock external processes with `IMlAgentsRunner`/`IMlAgentsProcessTracker` so tests do not spawn real ML jobs, and validate generated command lines plus cancellation handling. For integration smoke-tests, point the API at a disposable `ml-agents` checkout and run `curl` commands similar to those in the README, capturing logs in `logs/<runId>.txt` for troubleshooting.

## Commit & Pull Request Guidelines
Recent history uses Conventional Commit prefixes (e.g., `feat:`, `fix:`, `chore:`) followed by a short imperative summary—match that style and keep commits scoped to a single concern. Each PR should describe the change, reference related issues, and include screenshots or sample responses when altering HTTP shapes. Confirm `dotnet build` (and `dotnet test` when available) before requesting review, and call out any configuration changes that require teammates to update `appsettings.*`.

## Security & Configuration Tips
Never commit real Conda paths, Unity licenses, or ML-Agents models; keep them in local overrides. Validate that `UseCondaRun` and `CondaExecutable` settings line up before pushing so reviewers can reproduce behavior. When adding new configuration keys, document defaults in `MentorMlApi/README.md` and provide sensible fallbacks to prevent accidental process launches with undefined arguments.
