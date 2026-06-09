import { api, get, post } from './client';
import type {
  AgentChatRequest,
  AgentChatResponse,
  AgentSessionInfo,
  AgentSessionSummary,
  AgentSessionUpdateRequest,
  NovelProjectCreateRequest,
  NovelProjectDeleteResult,
  NovelProjectInfo,
  NovelProjectUpdateRequest,
  UserSettings,
} from './types';

// Agent Chat
export const sendChat = (req: AgentChatRequest) =>
  post<AgentChatResponse>('/agent/chat', req);

export const createAgentSession = () =>
  post<AgentSessionInfo>('/agent/session');

export const getAgentSession = (sessionId: string) =>
  get<AgentSessionInfo>(`/agent/session/${sessionId}`);

export const listAgentSessions = () =>
  get<AgentSessionSummary[]>('/agent/sessions');

export const updateAgentSession = (sessionId: string, req: AgentSessionUpdateRequest) =>
  api<AgentSessionInfo>(`/agent/session/${sessionId}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const rollbackStep = (sessionId: string, runId: string, stepId: string) =>
  post<{ success: boolean; message: string }>(`/agent/step/${sessionId}/rollback`, { runId, stepId });

export const createSseConnection = (sessionId: string): EventSource => {
  const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';
  return new EventSource(`${BASE_URL}/agent/sse/${sessionId}`);
};

// Novel Projects
export const createNovelProject = (req: NovelProjectCreateRequest) =>
  post<NovelProjectInfo>('/novel-projects', req);
export const activateNovelProject = (projectId: string) =>
  post<NovelProjectInfo>(`/novel-projects/${projectId}/activate`);
export const updateNovelProject = (projectId: string, req: NovelProjectUpdateRequest) =>
  api<NovelProjectInfo>(`/novel-projects/${projectId}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });
export const deleteNovelProject = (projectId: string) =>
  api<NovelProjectDeleteResult>(`/novel-projects/${projectId}`, { method: 'DELETE' });

// Settings
export const getSettings = () => get<UserSettings>('/settings');
export const saveSettings = (settings: Partial<UserSettings>) =>
  post<{ success: boolean; message: string }>('/settings', settings);
export const resetSettings = () =>
  post<UserSettings>('/settings/reset');
export const testConnection = (settings: Partial<UserSettings>) =>
  post<{ success: boolean; statusCode?: number; message: string }>('/settings/test-connection', {
    provider: settings.llmProvider,
    baseUrl: settings.llmBaseUrl,
    apiKey: settings.llmApiKey,
    model: settings.llmModel,
  });
