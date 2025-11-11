export interface MlAgentsRunRequest {
  runId?: string;
  configPath?: string;
  environmentPath?: string;
  noGraphics?: boolean;
  additionalArguments?: string[];
}

export interface MlAgentsRunResponse {
  command: string;
  workingDirectory: string;
  exitCode: number;
  startedAt: string;
  completedAt: string;
  standardOutput: string[];
  standardError: string[];
}

export type RequestStatus = 'idle' | 'submitting' | 'success' | 'error';