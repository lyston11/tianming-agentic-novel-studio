import { api, get, post } from './client';
import type {
  AgentChatRequest,
  AgentChatResponse,
  AgentSessionInfo,
  AgentSessionSummary,
  AgentSessionUpdateRequest,
  CharacterResponse,
  KnowledgeResponse,
  KnowledgeSearchResponse,
  MaterialContentResponse,
  MaterialListResponse,
  MaterialResponse,
  NovelProjectCreateRequest,
  NovelProjectDeleteResult,
  NovelProjectInfo,
  NovelProjectUpdateRequest,
  ProjectWorkflowDocument,
  StoryBibleResponse,
  StoryConstitutionResponse,
  UploadMaterialResponse,
  UserSettings,
  VolumeArcResponse,
  WorkspaceResponse,
} from './types';

// Materials API (new multi-user endpoints)
export const listMaterials = async (projectId: string): Promise<MaterialListResponse> => {
  const materials = await get<MaterialResponse[]>(`/materials?projectId=${encodeURIComponent(projectId)}`);
  return { materials, totalCount: materials.length };
};

export const getMaterialById = (id: string) =>
  get<MaterialResponse>(`/materials/${id}`);

export const getMaterialContent = (id: string) =>
  get<MaterialContentResponse>(`/materials/${id}/content`);

export const uploadMaterial = async (projectId: string, file: File, category: string, tags?: string) => {
  const formData = new FormData();
  formData.append('File', file);
  formData.append('ProjectId', projectId);
  formData.append('Title', file.name);
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

// StoryBible API (new multi-user endpoints)
export const getStoryBibleByProject = (projectId: string) =>
  get<StoryBibleResponse>(`/storybible?projectId=${encodeURIComponent(projectId)}`);

export const getConstitution = (projectId: string) =>
  get<StoryConstitutionResponse>(`/storybible/constitution?projectId=${encodeURIComponent(projectId)}`);

export const createOrUpdateConstitution = (req: { projectId: string; genre: string; subGenre?: string; coreHook: string; readerPromise?: string; genreProfile?: string; targetAudience?: string; taboos?: string }) =>
  post<StoryConstitutionResponse>('/storybible/constitution', req);

export const listCharacters = (projectId: string) =>
  get<CharacterResponse[]>(`/storybible/characters?projectId=${encodeURIComponent(projectId)}`);

export const createCharacter = (req: { projectId: string; name: string; role: string; alias?: string; age?: number; gender?: string; appearance?: string; personality?: string; background?: string; coreGoal?: string; motivation?: string }) =>
  post<CharacterResponse>('/storybible/characters', req);

export const updateCharacterById = (id: string, req: { name?: string; role?: string; alias?: string; age?: number; gender?: string; appearance?: string; personality?: string; background?: string; coreGoal?: string; motivation?: string }) =>
  api<CharacterResponse>(`/storybible/characters/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteCharacterById = (id: string) =>
  api<void>(`/storybible/characters/${id}`, { method: 'DELETE' });

// Workflow API (new multi-user endpoints)
export const listVolumeArcs = (projectId: string) =>
  get<VolumeArcResponse[]>(`/workflow/volumes?projectId=${encodeURIComponent(projectId)}`);

export const getProjectWorkflow = (projectId: string) =>
  get<ProjectWorkflowDocument>(`/workflow/project/${encodeURIComponent(projectId)}`);

export const getVolumeArc = (id: string) =>
  get<VolumeArcResponse>(`/workflow/volumes/${id}`);

export const createVolumeArc = (req: { projectId: string; volumeNumber: number; volumeTitle: string; volumeTheme?: string; targetChapters?: number; act1Setup?: string; act2Confrontation?: string; act3Climax?: string; act4Resolution?: string; keyEvents?: string; majorConflict?: string; conflictEscalation?: string }) =>
  post<VolumeArcResponse>('/workflow/volumes', req);

export const updateVolumeArc = (id: string, req: { volumeTitle?: string; volumeTheme?: string; targetChapters?: number; currentChapters?: number; act1Setup?: string; act2Confrontation?: string; act3Climax?: string; act4Resolution?: string; keyEvents?: string; majorConflict?: string; conflictEscalation?: string; status?: string }) =>
  api<VolumeArcResponse>(`/workflow/volumes/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteVolumeArc = (id: string) =>
  api<void>(`/workflow/volumes/${id}`, { method: 'DELETE' });

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
  const stored = localStorage.getItem('auth-storage');
  const token = stored ? JSON.parse(stored).state?.token : null;
  const url = token
    ? `${BASE_URL}/agent/sse/${sessionId}?token=${encodeURIComponent(token)}`
    : `${BASE_URL}/agent/sse/${sessionId}`;
  return new EventSource(url);
};

// Novel Projects
export const createNovelProject = (req: NovelProjectCreateRequest) =>
  post<NovelProjectInfo>('/project', {
    title: req.title || '未命名新书',
    genre: req.genre,
    coreHook: req.seed,
  });
export const activateNovelProject = async (projectId: string) => {
  sessionStorage.setItem('currentProjectId', projectId);
  return get<NovelProjectInfo>(`/project/${projectId}`);
};
export const updateNovelProject = (projectId: string, req: NovelProjectUpdateRequest) =>
  api<NovelProjectInfo>(`/project/${projectId}`, {
    method: 'PUT',
    body: JSON.stringify(req),
  });
export const deleteNovelProject = async (projectId: string): Promise<NovelProjectDeleteResult> => {
  await api<void>(`/project/${projectId}`, { method: 'DELETE' });
  return { success: true, message: '项目已删除。', activeProjectId: '' };
};

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

// Workspace
export const getWorkspace = () => get<WorkspaceResponse>('/workspace');
