Repository Guidelines
=====================

Project Structure & Module Organization
---------------------------------------
- `mentor-api/`: .NET minimal API for training orchestration; key entry `Program.cs`. Build outputs in `bin/`, `obj/`.
- `mentor-webapp/`: Static HTML/JS dashboard (`index.html`) served as a single page; assets like `favicon.svg`.
- `mentor-mcp/`: MCP server proxying mentor-api; TypeScript source in `src/`, compiled artifacts in `dist/`.
- Supporting folders: `.github/` workflows, `.vscode/` editor settings, `ml-agents-training-results/` sample outputs.

Build, Test, and Development Commands
-------------------------------------
- mentor-api: `dotnet build` (compile) and `dotnet run` (start API on localhost:5113).
- mentor-webapp: open `mentor-webapp/index.html` in a browser (no build step).
- mentor-mcp: `npm install` (deps), `npm run build` (tsc compile), `npm run dev` (ts-node for local dev), `npm start` (run built server).
- Process checks: call mentor-api endpoints directly, e.g., `curl http://localhost:5113/health`.

Coding Style & Naming Conventions
---------------------------------
- C#: follow idiomatic .NET formatting (4-space indent, PascalCase for types/methods, camelCase locals); nullability respected.
- TypeScript: ES modules, TypeScript strictness, single-responsibility helpers; prefer `const` and explicit schemas (zod).
- Webapp JS: inline script in `index.html`; keep functions small, avoid global leaks beyond existing `state`.
- Filenames: keep kebab-case for assets, PascalCase for C# files, and lower-case commands.

Testing Guidelines
------------------
- mentor-api: invoke API endpoints with realistic payloads; validate responses and log output. Add unit/integration tests if expanding logic.
- mentor-mcp: after changes, run `npm run build` and sanity-check tool calls against a live mentor-api.
- Webapp: manual verification via browser for rendering and API interactions; no automated tests present.

Commit & Pull Request Guidelines
--------------------------------
- Commit messages: use concise present-tense summaries (e.g., “Add tensorboard start tool to MCP”).
- Pull requests: include a short description, screenshots for UI changes (webapp), reproduction/validation steps, and linked issues if applicable.
