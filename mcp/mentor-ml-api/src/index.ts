import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import {
  MentorMlApiClient,
  MlAgentsProcessStatus,
  mlAgentsProcessStatusSchema,
  mlAgentsRunRequestShape,
  mlAgentsRunResponseShape
} from "./mentorApiClient";

const baseUrl = process.env.MENTOR_ML_API_BASE_URL ?? "http://localhost:5113";
const timeoutMs = parseEnvNumber(process.env.MENTOR_ML_API_TIMEOUT_MS);
const apiKey = process.env.MENTOR_ML_API_API_KEY;
const authHeader = process.env.MENTOR_ML_API_AUTHORIZATION;
const extraHeaders: Record<string, string> = {};

if (authHeader) {
  extraHeaders.Authorization = authHeader;
}

const client = new MentorMlApiClient(baseUrl, {
  apiKey,
  timeoutMs,
  extraHeaders: Object.keys(extraHeaders).length ? extraHeaders : undefined
});

const server = new McpServer({
  name: "mentor-ml-api",
  version: "0.1.0",
  description: "Expose Mentor ML API endpoints as MCP tools"
});

const emptyInputShape = {} as const satisfies z.ZodRawShape;
const listProcessesOutputShape = {
  processes: z.array(mlAgentsProcessStatusSchema)
} as const satisfies z.ZodRawShape;

type ToolResult = {
  content: { type: "text"; text: string }[];
  structuredContent?: Record<string, unknown>;
  isError?: boolean;
};

const toText = (value: unknown): string => JSON.stringify(value, null, 2);

const handleToolError = (error: unknown): ToolResult => ({
  content: [
    {
      type: "text",
      text: `Mentor ML API tool failed: ${error instanceof Error ? error.message : String(error)}`
    }
  ],
  isError: true
});

server.registerTool(
  "mentor_run_training",
  {
    title: "Run ML-Agents training",
    description: "Calls POST /mlagents/run with the supplied payload.",
    inputSchema: mlAgentsRunRequestShape,
    outputSchema: mlAgentsRunResponseShape
  },
  async (args): Promise<ToolResult> => {
    try {
      const response = await client.runTraining(args);
      return {
        content: [
          {
            type: "text",
            text: toText({
              exitCode: response.exitCode,
              command: response.command,
              startedAt: response.startedAt,
              completedAt: response.completedAt,
              workingDirectory: response.workingDirectory
            })
          }
        ],
        structuredContent: response
      };
    } catch (error) {
      return handleToolError(error);
    }
  }
);

server.registerTool(
  "mentor_list_processes",
  {
    title: "List running ML-Agents processes",
    description: "Calls GET /mlagents/processes to show currently running jobs.",
    inputSchema: emptyInputShape,
    outputSchema: listProcessesOutputShape
  },
  async (): Promise<ToolResult> => {
    try {
      const processes = await client.listProcesses();
      const structuredContent: { processes: MlAgentsProcessStatus[] } = { processes };

      return {
        content: [
          {
            type: "text",
            text: processes.length
              ? toText(processes)
              : "No Mentor ML API processes are currently tracked."
          }
        ],
        structuredContent
      };
    } catch (error) {
      return handleToolError(error);
    }
  }
);

async function main(): Promise<void> {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch(error => {
  console.error("Failed to start Mentor ML API MCP server", error);
  process.exitCode = 1;
});

function parseEnvNumber(value: string | undefined): number | undefined {
  if (!value) {
    return undefined;
  }

  const parsed = Number(value);
  if (Number.isNaN(parsed)) {
    console.warn(
      `Ignoring invalid numeric value '${value}' supplied to MENTOR_ML_API_TIMEOUT_MS.`
    );
    return undefined;
  }

  return parsed;
}

