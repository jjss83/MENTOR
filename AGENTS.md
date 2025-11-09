# Repository Guidelines

## Project Structure & Module Organization
Source lives under src/: MENTOR.Core hosts domain models, interfaces, and services; MENTOR.API exposes the Minimal API entry point in Program.cs; MENTOR.Infrastructure wraps LiteDB, ML-Agents process control, and filesystem helpers. Tests sit in 	ests/MENTOR.Core.Tests and 	ests/MENTOR.API.Tests, mirroring the namespaces they cover. Docs and architecture notes stay in docs/, while runnable configuration samples belong in examples/. Keep assets that ship with the API near the project that consumes them and reference them via relative paths to avoid brittle tooling.

## Build, Test & Development Commands
Run dotnet restore once per environment to hydrate packages. dotnet build MENTOR.sln validates the full solution; use dotnet run --project src/MENTOR.API for the HTTP service (default http://localhost:5000). During feature work, dotnet watch run --project src/MENTOR.API gives hot reload. Execute every test suite via dotnet test at the repo root, and add /p:CollectCoverage=true when reporting coverage. Use dotnet format before opening a PR to enforce analyzers and EditorConfig rules.

## Coding Style & Naming Conventions
Target C# 12/.NET 8 with nullable reference types enabled. Prefer 4-space indentation, expression-bodied members only when they aid readability, and ar for obvious types. Classes, records, and public methods use PascalCase; private fields and locals stay camelCase; interfaces keep the I prefix. Keep one class or record per file under matching folders (e.g., Services/TrainingService.cs). Guard clauses go at the top of methods, async APIs return Task/Task<T>, and immutable data should be a ecord. Log with Serilog abstractions injected via DI—no static state.

## Testing Guidelines
xUnit + FluentAssertions power all unit tests. Name files <TypeUnderTest>Tests.cs and methods MethodUnderTest_ShouldExpectedBehavior. Place unit tests alongside the corresponding project in 	ests/ and keep ML-Agents process calls mocked. Maintain high-value coverage on orchestrators and adapters; when coverage drops below 80% for a module, add regression tests before merging. Use dotnet test --filter for targeted runs when iterating.

## Commit & Pull Request Guidelines
Commits follow the existing imperative style (Add initial project specification). Keep changes scoped, reference tickets in the body, and include why a change matters. Pull requests need: summary of intent, validation steps (commands run, logs if relevant), linked issues, and updated docs/config snapshots when behavior shifts. Attach screenshots or JSON samples whenever API contracts or configuration surfaces change, and call out any migration steps for agents deploying the service.
