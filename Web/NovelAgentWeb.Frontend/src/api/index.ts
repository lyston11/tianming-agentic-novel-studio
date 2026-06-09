import { api, get, post } from './client';
import type {
  AgentChatRequest,
  AgentChatResponse,
  AgentSessionInfo,
  AgentSessionSummary,
  AgentSessionUpdateRequest,
  KnowledgeResponse,
  KnowledgeSearchResponse,
  MaterialContentResponse,
  MaterialListResponse,
  MaterialResponse,
  NovelProjectCreateRequest,
  NovelProjectDeleteResult,
  NovelProjectInfo,
  NovelProjectUpdateRequest,
  UploadMaterialResponse,
  UserSettings,
} from './types';

// Materials API (new multi-user endpoints)
export const listMaterials = (projectId: string) =>
  get<MaterialListResponse>(`/materials?projectId=${encodeURIComponent(projectId)}`);

export const getMaterialById = (id: string) =>
  get<MaterialResponse>(`/materials/${id}`);

export const getMaterialContent = (id: string) =>
  get<MaterialContentResponse>(`/materials/${id}/content`);

export const uploadMaterial = async (projectId: string, file: File, category: string, tags?: string) => {
  const formData = new FormData();
  formData.append('File', file);
  formData.append('ProjectId', projectId);
  formData.append('Category', category);
  if (tags) formData.append('Tags', tags);

  return api<UploadMaterialResponse>('/materials/upload', {
    method: 'POST',
    body: formData,
  });
};

export const createMaterialFromText = (req: { projectId: string; title: string; content: string; category: string; tags?: string }) =>
  post<MaterialResponse>('/materials', req);

export const updateMaterialById = (id: string, req: { title?: string; category?: string; tags?: string }) =>
  api<MaterialResponse>(`/materials/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteMaterialById = (id: string) =>
  api<void>(`/materials/${id}`, { method: 'DELETE' });

// Knowledge API (new multi-user endpoints)
export const searchKnowledgeEntries = (req: { projectId: string; query: string; topK?: number; category?: string }) =>
  post<KnowledgeSearchResponse>('/knowledge/search', req);

export const listKnowledgeEntries = (projectId: string, category?: string) =>
  get<KnowledgeResponse[]>(`/knowledge?projectId=${encodeURIComponent(projectId)}${category ? `&category=${encodeURIComponent(category)}` : ''}`);

export const createKnowledgeEntry = (req: { projectId: string; title: string; content: string; category: string; tags?: string; sourceType?: string; sourceId?: string }) =>
  post<KnowledgeResponse>('/knowledge', req);

export const updateKnowledgeEntryById = (id: string, req: { title?: string; content?: string; category?: string; tags?: string }) =>
  api<KnowledgeResponse>(`/knowledge/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteKnowledgeEntryById = (id: string) =>
  api<void>(`/knowledge/${id}`, { method: 'DELETE' });

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
