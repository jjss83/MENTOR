import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { z } from "zod";

const baseUrl = (process.env.MENTOR_API_BASE_URL ?? "http://localhost:5113").replace(/\/$/, "");

const server = new McpServer({
  name: "mentor-mcp",
  version: "1.0.0",
});

const trainingSchema = z.object({
  envPath: z.string().optional(),
  config: z.string().optional(),
  runId: z.string().optional(),
  resultsDir: z.string().optional(),
  condaEnv: z.string().optional(),
  basePort: z.number().int().optional(),
  noGraphics: z.boolean().optional(),
  skipConda: z.boolean().optional(),
  tensorboard: z.boolean().optional(),
  resume: z.boolean().optional(),
});

const resumeFlagSchema = z.object({
  runId: z.string(),
  resumeOnStart: z.boolean(),
  resultsDir: z.string().optional(),
});

const trainStatusSchema = z.object({
  runId: z.string().optional(),
  resultsDir: z.string().optional(),
});

const processStatusSchema = z.object({
  resultsDir: z.string().optional(),
});

const killProcessSchema = z.object({
  executable: z.string(),
  resultsDir: z.string().optional(),
});

const resumeSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
  basePort: z.number().int().optional(),
});

const runControlSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
});

const archiveSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
});

const deleteRunSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
  confirm: z.boolean().default(true),
});

const tensorboardStartSchema = z.object({
  resultsDir: z.string().optional(),
  runId: z.string().optional(),
  condaEnv: z.string().optional(),
  skipConda: z.boolean().optional(),
  port: z.number().int().optional(),
});

const tensorboardStatusSchema = z.object({
  resultsDir: z.string().optional(),
});

server.registerTool(
  "health",
  { description: "Check mentor-api health", inputSchema: z.object({}) },
  async () => {
    const response = await getJson<Record<string, unknown>>("/health");
    return asText(JSON.stringify(response, null, 2));
  }
);

server.registerTool(
  "dashboard-status",
  {
    description: "Check mentor web dashboard exposure status",
    inputSchema: z.object({}),
  },
  async () => {
    const json = await getJson<unknown>("/dashboard/status");
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "dashboard-start",
  {
    description: "Ensure mentor web dashboard is served over http://localhost:4173",
    inputSchema: z.object({}),
  },
  async () => {
    const json = await getJson<unknown>("/dashboard/start");
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "dashboard-stop",
  {
    description: "Stop the mentor web dashboard server",
    inputSchema: z.object({}),
  },
  async () => {
    const json = await postJson<unknown>("/dashboard/stop", {});
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "train",
  {
    description: "Start mentor-cli training via mentor-api and return run info",
    inputSchema: trainingSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/train", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "resume-flag",
  {
    description: "Update resume-on-start flag for a training run via mentor-api",
    inputSchema: resumeFlagSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/train/resume-flag", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "train-stop",
  {
    description: "Gracefully stop a running training session via mentor-api (marks it resumable)",
    inputSchema: runControlSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/train/stop", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "train-resume",
  {
    description: "Resume a stopped or unfinished training session via mentor-api",
    inputSchema: resumeSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/train/resume", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "train-archive",
  {
    description: "Archive a completed mentor-cli training run via mentor-api",
    inputSchema: archiveSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/train/archive", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "train-delete",
  {
    description: "Delete a mentor-cli training run (stops if running, removes files) via mentor-api. Confirmation is required.",
    inputSchema: deleteRunSchema,
  },
  async (input) => {
    const body = normalizeBody({ ...input, confirm: true });
    const json = await postJson<unknown>("/train/delete", body);
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "train-status",
  {
    description: "Check mentor-cli training status via mentor-api",
    inputSchema: trainStatusSchema,
  },
  async (input) => {
    const query = buildQuery({ resultsDir: input.resultsDir });
    const path = input.runId
      ? `/train-status/${encodeURIComponent(input.runId)}${query}`
      : `/train-status${query}`;
    const json = await getJson<unknown>(path);
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "process-status",
  {
    description: "Report mlagents-learn process count and running env executable counts via mentor-api",
    inputSchema: processStatusSchema,
  },
  async (input) => {
    const query = buildQuery({ resultsDir: input.resultsDir });
    const json = await getJson<unknown>(`/process-status${query}`);
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "process-kill",
  {
    description: "Kill all running processes for a given executable (mlagents-learn or tracked env executables) via mentor-api",
    inputSchema: killProcessSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/process-kill", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "tensorboard-start",
  {
    description: "Start TensorBoard via mentor-api (uses defaults if no arguments are provided)",
    inputSchema: tensorboardStartSchema,
  },
  async (input) => {
    const query = buildQuery({
      resultsDir: input.resultsDir,
      runId: input.runId,
      condaEnv: input.condaEnv,
      skipConda: input.skipConda,
      port: input.port,
    });
    const json = await getJson<unknown>(`/tensorboard/start${query}`);
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "tensorboard-status",
  {
    description: "Check TensorBoard status via mentor-api",
    inputSchema: tensorboardStatusSchema,
  },
  async (input) => {
    const query = buildQuery({ resultsDir: input.resultsDir });
    const json = await getJson<unknown>(`/tensorboard/status${query}`);
    return asText(JSON.stringify(json, null, 2));
  }
);

server.server.onerror = (error) => {
  console.error("mentor-mcp server error", error);
};

const transport = new StdioServerTransport();
await server.connect(transport);

function asText(text: string): CallToolResult {
  return { content: [{ type: "text", text }] };
}

function normalizeBody<T extends Record<string, unknown>>(body: T): T {
  const clone: Record<string, unknown> = { ...body };
  for (const key of Object.keys(clone)) {
    const value = clone[key];
    if (value === "") {
      clone[key] = undefined;
    }
  }

  return clone as T;
}

function buildQuery(params: Record<string, string | number | boolean | undefined>): string {
  const entries = Object.entries(params).filter(([, value]) => value !== undefined && value !== null);
  if (!entries.length) {
    return "";
  }
  const search = entries
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
    .join("&");
  return `?${search}`;
}

async function postJson<TResponse>(path: string, body: unknown): Promise<TResponse> {
  const response = await fetch(`${baseUrl}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  const text = await response.text();
  if (!response.ok) {
    throw new Error(formatError(path, response.status, response.statusText, text));
  }

  if (!text) {
    return {} as TResponse;
  }

  try {
    return JSON.parse(text) as TResponse;
  } catch (err) {
    throw new Error(`Expected JSON from ${path} but got: ${text}`);
  }
}

async function getJson<TResponse>(path: string): Promise<TResponse> {
  const response = await fetch(`${baseUrl}${path}`);
  const text = await response.text();
  if (!response.ok) {
    throw new Error(formatError(path, response.status, response.statusText, text));
  }

  if (!text) {
    return {} as TResponse;
  }

  return JSON.parse(text) as TResponse;
}

function formatError(path: string, status: number, statusText: string, body: string) {
  return `${path} failed: ${status} ${statusText}${body ? `\n${body}` : ""}`;
}
