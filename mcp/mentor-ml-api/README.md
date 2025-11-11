# Mentor ML API MCP Server

A lightweight [Model Context Protocol](https://modelcontextprotocol.io/) server that exposes the Mentor ML API endpoints as MCP tools so assistants can start ML-Agents training runs or inspect running processes without bespoke glue scripts.

## Prerequisites

- Node.js 18+ (Node 22 LTS is recommended)
- An instance of the Mentor ML API running locally (defaults to `http://localhost:5113`)
- npm for installing dependencies

## Installation

```powershell
cd X:\workspace\MENTOR\mcp\mentor-ml-api
npm install
```

## Building & Running

```powershell
# Compile TypeScript once
npm run build

# Launch the MCP server over stdio
npm start
```

The server speaks the stdio transport, so you can point MCP-compatible clients (Claude Desktop, MCP Inspector, etc.) at `node X:\workspace\MENTOR\mcp\mentor-ml-api\dist\index.js`.

For faster inner-loop development you can run `npm run dev` to keep `tsc` in watch mode.

## Configuration

Environment variables control how the server talks to Mentor ML API:

| Variable | Default | Description |
| --- | --- | --- |
| `MENTOR_ML_API_BASE_URL` | `http://localhost:5113` | Base URL for the Mentor ML API instance. |
| `MENTOR_ML_API_API_KEY` | _(unset)_ | Optional value added as an `x-api-key` header for secured deployments. |
| `MENTOR_ML_API_AUTHORIZATION` | _(unset)_ | Optional raw `Authorization` header (for bearer/JWT tokens). |
| `MENTOR_ML_API_TIMEOUT_MS` | `30000` | Request timeout in milliseconds when proxying to the API. |

Set these before launching the server (e.g. via a `.env` manager or your MCP client configuration).

## Available Tools

| Tool | Description | Input Schema |
| --- | --- | --- |
| `mentor_run_training` | Calls `POST /mlagents/run` with the provided payload to start a new ML-Agents training job. | Matches the body of `MlAgentsRunRequest` (all fields optional so defaults from the API still apply). |
| `mentor_list_processes` | Calls `GET /mlagents/processes` to list the currently tracked trainer processes. | No arguments. |

Responses include a concise text summary plus structured JSON so MCP clients can inspect exit codes, commands, timestamps, and tracked processes.

## Scripts

| Command | Purpose |
| --- | --- |
| `npm run build` | Compile TypeScript sources to `dist/`. |
| `npm start` | Start the stdio MCP server from the compiled output. |
| `npm run dev` | Watch-mode compilation for iterative development. |
| `npm run type-check` | TypeScript type checking without emitting files. |
| `npm run clean` | Remove the `dist/` output directory. |

## Development Notes

- The server uses `@modelcontextprotocol/sdk` for schema validation and stdio transport wiring.
- Tool schemas stay in sync with the Mentor ML API contracts via the shared Zod definitions in `src/mentorApiClient.ts`.
- Errors from the Mentor API are surfaced back to the MCP client so you see the HTTP status plus body text when something fails.
