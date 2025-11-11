import { z } from "zod";

export const mlAgentsRunRequestShape = {
  configPath: z.string().trim().min(1).optional(),
  runId: z.string().trim().min(1).optional(),
  environmentPath: z.string().trim().min(1).optional(),
  noGraphics: z.boolean().optional(),
  curriculumPath: z.string().trim().min(1).optional(),
  keepCheckpoints: z.number().int().positive().optional(),
  lesson: z.number().int().nonnegative().optional(),
  loadModel: z.boolean().optional(),
  numRuns: z.number().int().positive().optional(),
  saveFrequency: z.number().int().positive().optional(),
  seed: z.number().int().optional(),
  slow: z.boolean().optional(),
  train: z.boolean().optional(),
  workerId: z.number().int().nonnegative().optional(),
  dockerTargetName: z.string().trim().min(1).optional(),
  additionalArguments: z.array(z.string().trim().min(1)).optional()
} satisfies z.ZodRawShape;

export const mlAgentsRunRequestSchema = z.object(mlAgentsRunRequestShape).strict();

export type MlAgentsRunRequest = z.infer<typeof mlAgentsRunRequestSchema>;

export const mlAgentsRunResponseShape = {
  command: z.string(),
  workingDirectory: z.string(),
  exitCode: z.number(),
  startedAt: z.string(),
  completedAt: z.string(),
  standardOutput: z.array(z.string()),
  standardError: z.array(z.string())
} satisfies z.ZodRawShape;

export const mlAgentsRunResponseSchema = z.object(mlAgentsRunResponseShape);

export type MlAgentsRunResponse = z.infer<typeof mlAgentsRunResponseSchema>;

export const mlAgentsProcessStatusShape = {
  id: z.string(),
  runId: z.string(),
  processId: z.number(),
  command: z.string(),
  workingDirectory: z.string(),
  startedAt: z.string(),
  elapsed: z.string()
} satisfies z.ZodRawShape;

export const mlAgentsProcessStatusSchema = z.object(mlAgentsProcessStatusShape);

export type MlAgentsProcessStatus = z.infer<typeof mlAgentsProcessStatusSchema>;

export interface MentorMlApiClientOptions {
  apiKey?: string;
  timeoutMs?: number;
  extraHeaders?: Record<string, string>;
}

export class MentorMlApiClient {
  private readonly baseUrl: URL;
  private readonly defaultHeaders: Record<string, string>;
  private readonly timeoutMs: number;

  constructor(baseUrl: string, options?: MentorMlApiClientOptions) {
    try {
      this.baseUrl = new URL(baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`);
    } catch (error) {
      throw new Error(`Invalid Mentor ML API base URL '${baseUrl}': ${String(error)}`);
    }

    this.timeoutMs = options?.timeoutMs ?? 30_000;
    this.defaultHeaders = {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...options?.extraHeaders
    };

    if (options?.apiKey) {
      this.defaultHeaders["x-api-key"] = options.apiKey;
    }
  }

  async runTraining(payload: MlAgentsRunRequest): Promise<MlAgentsRunResponse> {
    const body = mlAgentsRunRequestSchema.parse(payload ?? {});
    return this.post("mlagents/run", body, mlAgentsRunResponseSchema);
  }

  async listProcesses(): Promise<MlAgentsProcessStatus[]> {
    return this.get(
      "mlagents/processes",
      z.array(mlAgentsProcessStatusSchema)
    );
  }

  private async post<T>(path: string, body: unknown, schema: z.ZodType<T>): Promise<T> {
    return this.request(path, {
      method: "POST",
      body: JSON.stringify(body)
    }, schema);
  }

  private async get<T>(path: string, schema: z.ZodType<T>): Promise<T> {
    return this.request(path, { method: "GET" }, schema);
  }

  private async request<T>(path: string, init: RequestInit, schema: z.ZodType<T>): Promise<T> {
    const url = new URL(path, this.baseUrl);
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await fetch(url, {
        ...init,
        headers: {
          ...this.defaultHeaders,
          ...(init.headers as Record<string, string> | undefined)
        },
        signal: controller.signal
      });

      if (!response.ok) {
        const errorBody = await response.text();
        throw new Error(
          `Mentor ML API request failed (${response.status} ${response.statusText}): ${errorBody}`
        );
      }

      const data = await response.json();
      return schema.parse(data);
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") {
        throw new Error(
          `Mentor ML API request timed out after ${this.timeoutMs}ms while calling ${path}`
        );
      }

      throw error instanceof Error
        ? error
        : new Error(`Unexpected error calling Mentor ML API: ${String(error)}`);
    } finally {
      clearTimeout(timeout);
    }
  }
}
