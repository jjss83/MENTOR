import type { MlAgentsRunRequest, MlAgentsRunResponse } from './types';

export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '')
  ?? 'https://localhost:7136';

export async function runMlAgents(request: MlAgentsRunRequest): Promise<MlAgentsRunResponse> {
  const response = await fetch(`${API_BASE_URL}/mlagents/run`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(request ?? {})
  });

  if (!response.ok) {
    const payload = await safeParse(response);
    const message = typeof payload?.title === 'string'
      ? `${response.status} ${response.statusText}: ${payload.title}`
      : `${response.status} ${response.statusText}`;
    throw new Error(message);
  }

  return response.json();
}

async function safeParse(response: Response): Promise<Record<string, unknown> | null> {
  try {
    return await response.json();
  }
  catch {
    return null;
  }
}