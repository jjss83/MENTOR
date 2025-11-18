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
  envPath: z.string(),
  config: z.string(),
  runId: z.string().optional(),
  resultsDir: z.string().optional(),
  condaEnv: z.string().optional(),
  basePort: z.number().int().optional(),
  noGraphics: z.boolean().optional(),
  skipConda: z.boolean().optional(),
  tensorboard: z.boolean().optional(),
});

const trainStatusSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
});

const reportSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
});

const interpreterSchema = z.object({
  runId: z.string(),
  resultsDir: z.string().optional(),
  prompt: z.string().optional(),
  openAiModel: z.string().optional(),
  openAiApiKey: z.string().optional(),
  checkOpenAi: z.boolean().optional(),
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
  "train-status",
  {
    description: "Check mentor-cli training status via mentor-api",
    inputSchema: trainStatusSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/train-status", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "report",
  {
    description: "Emit the mentor-cli report output for a run",
    inputSchema: reportSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/report", normalizeBody(input));
    return asText(JSON.stringify(json, null, 2));
  }
);

server.registerTool(
  "report-interpreter",
  {
    description: "Generate the report-interpreter payload (and optional OpenAI call)",
    inputSchema: interpreterSchema,
  },
  async (input) => {
    const json = await postJson<unknown>("/report-interpreter", normalizeBody(input));
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