import { api, buildActionIdempotencyKey, get } from './client';
import type {
  AgentRuntimeEventView,
  AgentSessionInfo,
  AgentSessionResumeResponse,
  AgentSessionSummary,
  AgentSessionUpdateRequest,
  RuntimeActiveRunDto,
} from './types';

export const createAgentSession = () =>
  api<AgentSessionInfo>('/agent/session', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildActionIdempotencyKey('agent-session') },
  });

export const getAgentSession = (sessionId: string) =>
  get<AgentSessionInfo>(`/agent/session/${sessionId}`);

export const resumeAgentSession = (sessionId: string) =>
  get<AgentSessionResumeResponse>(`/agent/sessions/${encodeURIComponent(sessionId)}/resume`);

export const listAgentSessions = () =>
  get<AgentSessionSummary[]>('/agent/sessions');

export const updateAgentSession = (sessionId: string, req: AgentSessionUpdateRequest) =>
  api<AgentSessionInfo>(`/agent/session/${sessionId}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const getSessionActiveRuntimeRun = (sessionId: string) =>
  get<RuntimeActiveRunDto>(`/runtime/sessions/${encodeURIComponent(sessionId)}/active-run`);

export const listRuntimeEvents = (req: {
  sessionId: string;
  userId?: string;
  runId?: string | null;
  limit?: number;
  afterEventId?: string | null;
}) => {
  const params = new URLSearchParams();
  if (req.runId) params.set('runId', req.runId);
  if (req.userId) params.set('userId', req.userId);
  params.set('sessionId', req.sessionId);
  if (req.limit) params.set('limit', String(req.limit));
  if (req.afterEventId) params.set('afterEventId', req.afterEventId);
  return get<AgentRuntimeEventView[]>(`/runtime/events?${params.toString()}`);
};
