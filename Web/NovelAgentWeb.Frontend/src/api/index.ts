import { API_BASE_URL, ApiError, api, get, post } from './client';
import { readStoredAuthToken } from '../services/authStorage';
import type {
  AgentChatRequest,
  AgentChatResponse,
  AgentSessionInfo,
  AgentSessionResumeResponse,
  AgentSessionSummary,
  AgentSessionUpdateRequest,
  ChapterCreateRequest,
  ChapterResponse,
  ChapterVersionCompareResponse,
  ChapterVersionResponse,
  CreateCreativeIntentRequest,
  CreativeIntentItem,
  CreativeIntentQueryResult,
  DecideCreativeIntentRequest,
  KnowledgeDirectoryResponse,
  CharacterResponse,
  KnowledgeResponse,
  KnowledgeSearchResult,
  LlmConnectionHealth,
  MaterialContentResponse,
  MaterialListResponse,
  MaterialResponse,
  NovelProjectCreateRequest,
  NovelProjectDeleteResult,
  NovelProjectInfo,
  NovelProjectUpdateRequest,
  ProjectWorkflowDocument,
  RuntimeActiveRunDto,
  AgentRuntimeEventView,
  RuntimeRunDto,
  StoryBibleResponse,
  StoryConstitutionResponse,
  UploadMaterialResponse,
  UserSettings,
  VolumeArcResponse,
  WorkspaceResponse,
} from './types';

export { API_BASE_URL, ApiError };

const buildStableIdempotencyKey = (prefix: string, payload: unknown): string => {
  const stable = stableJson(payload);
  let hash = 2166136261;
  for (let i = 0; i < stable.length; i += 1) {
    hash ^= stable.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }

  return `${prefix}-${(hash >>> 0).toString(16)}`;
};

const buildActionIdempotencyKey = (prefix: string): string => {
  const randomId = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

  return `${prefix}-${randomId}`;
};

const stableJson = (value: unknown): string => {
  if (Array.isArray(value)) {
    return `[${value.map(stableJson).join(',')}]`;
  }
  if (value && typeof value === 'object') {
    return `{${Object.entries(value as Record<string, unknown>)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, item]) => `${JSON.stringify(key)}:${stableJson(item)}`)
      .join(',')}}`;
  }

  return JSON.stringify(value) ?? 'null';
};

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
    headers: {
      'Idempotency-Key': buildStableIdempotencyKey('material-upload', {
        projectId,
        fileName: file.name,
        size: file.size,
        lastModified: file.lastModified,
        category,
        tags,
      }),
    },
    body: formData,
  });
};

export const createMaterialFromText = (req: { projectId: string; title: string; content: string; category: string; tags?: string }) =>
  api<MaterialResponse>('/materials', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('material', req) },
    body: JSON.stringify(req),
  });

export const updateMaterialById = (id: string, req: { title?: string; category?: string; tags?: string }) =>
  api<MaterialResponse>(`/materials/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteMaterialById = (id: string) =>
  api<void>(`/materials/${id}`, { method: 'DELETE' });

// Knowledge API (new multi-user endpoints)
export const searchKnowledgeEntries = (req: { projectId: string; query: string; topK?: number; entryType?: string }) =>
  post<KnowledgeSearchResult[]>('/knowledge/search', req);

export const listKnowledgeEntries = (projectId?: string) =>
  projectId?.trim()
    ? get<KnowledgeResponse[]>(`/knowledge?projectId=${encodeURIComponent(projectId.trim())}`)
    : get<KnowledgeResponse[]>('/knowledge');

export const listKnowledgeDirectories = () =>
  get<KnowledgeDirectoryResponse[]>('/knowledge/directories');

export const createKnowledgeDirectory = (req: { name: string }) =>
  api<KnowledgeDirectoryResponse>('/knowledge/directories', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('knowledge-directory', req) },
    body: JSON.stringify(req),
  });

export const updateKnowledgeDirectory = (key: string, req: { name: string }) =>
  api<KnowledgeDirectoryResponse>(`/knowledge/directories/${encodeURIComponent(key)}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteKnowledgeDirectory = (key: string) =>
  api<void>(`/knowledge/directories/${encodeURIComponent(key)}`, { method: 'DELETE' });

export const createKnowledgeEntry = (req: { projectId: string; title: string; content: string; entryType: string; tags?: string[]; weight?: number }) =>
  api<KnowledgeResponse>('/knowledge', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('knowledge-entry', req) },
    body: JSON.stringify(req),
  });

export const updateKnowledgeEntryById = (id: string, req: { entryType?: string; title?: string; content?: string; tags?: string[]; weight?: number; isArchived?: boolean }) =>
  api<KnowledgeResponse>(`/knowledge/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteKnowledgeEntryById = (id: string) =>
  api<void>(`/knowledge/${id}`, { method: 'DELETE' });

export const uploadKnowledgeFile = (projectId: string, file: File, title?: string) => {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('projectId', projectId);
  if (title) formData.append('title', title);

  return api<{ taskId: string; status: string; message: string }>('/knowledge/upload', {
    method: 'POST',
    body: formData,
  });
};

export const getKnowledgeTask = (taskId: string) =>
  get<{ id: string; status: string; progress: number; extractedEntriesCount: number; errorMessage?: string }>(
    `/knowledge/tasks/${encodeURIComponent(taskId)}`
  );

// Creative intent API
export const listCreativeIntents = (projectId: string, status?: string, targetChapterId?: string, limit?: number) => {
  const params = new URLSearchParams({ projectId });
  if (status) params.set('status', status);
  if (targetChapterId) params.set('targetChapterId', targetChapterId);
  if (limit) params.set('limit', String(limit));
  return get<CreativeIntentQueryResult>(`/creative/intents?${params.toString()}`);
};

export const createCreativeIntent = (req: CreateCreativeIntentRequest) =>
  api<CreativeIntentItem>('/creative/intents', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('creative-intent', req) },
    body: JSON.stringify(req),
  });

export const decideCreativeIntent = (intentId: string, req: DecideCreativeIntentRequest) =>
  api<CreativeIntentItem>(`/creative/intents/${encodeURIComponent(intentId)}/decision`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

// StoryBible API (new multi-user endpoints)
export const getStoryBibleByProject = (projectId: string) =>
  get<StoryBibleResponse>(`/storybible?projectId=${encodeURIComponent(projectId)}`);

export const getConstitution = (projectId: string) =>
  get<StoryConstitutionResponse>(`/storybible/constitution?projectId=${encodeURIComponent(projectId)}`);

export const createOrUpdateConstitution = (req: { projectId: string; genre: string; subGenre?: string; coreHook: string; readerPromise?: string; genreProfile?: string; targetAudience?: string; taboos?: string }) =>
  api<StoryConstitutionResponse>('/storybible/constitution', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('story-constitution', req) },
    body: JSON.stringify(req),
  });

export const listCharacters = (projectId: string) =>
  get<CharacterResponse[]>(`/storybible/characters?projectId=${encodeURIComponent(projectId)}`);

export const createCharacter = (req: { projectId: string; name: string; role: string; alias?: string; age?: number; gender?: string; appearance?: string; personality?: string; background?: string; coreGoal?: string; motivation?: string }) =>
  api<CharacterResponse>('/storybible/characters', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('character', req) },
    body: JSON.stringify(req),
  });

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
  api<VolumeArcResponse>('/workflow/volumes', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('volume-arc', req) },
    body: JSON.stringify(req),
  });

export const updateVolumeArc = (id: string, req: { volumeTitle?: string; volumeTheme?: string; targetChapters?: number; currentChapters?: number; act1Setup?: string; act2Confrontation?: string; act3Climax?: string; act4Resolution?: string; keyEvents?: string; majorConflict?: string; conflictEscalation?: string; status?: string }) =>
  api<VolumeArcResponse>(`/workflow/volumes/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteVolumeArc = (id: string) =>
  api<void>(`/workflow/volumes/${id}`, { method: 'DELETE' });

// Agent Chat
export const sendChat = (req: AgentChatRequest) =>
  api<AgentChatResponse>('/agent/chat', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildActionIdempotencyKey('agent-chat') },
    body: JSON.stringify(req),
  });

export const createAgentSession = (projectId?: string | null) =>
  api<AgentSessionInfo>(`/agent/session${projectId ? `?projectId=${encodeURIComponent(projectId)}` : ''}`, {
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

export const cancelRuntimeRun = (runId: string) =>
  api<RuntimeRunDto>(`/runtime/runs/${encodeURIComponent(runId)}/cancel`, {
    method: 'POST',
  });

export interface SseStreamConnection {
  onopen?: () => void;
  onmessage?: (event: MessageEvent<string>) => void;
  onerror?: () => void;
  close: () => void;
}

const dispatchSseBlock = (connection: SseStreamConnection, block: string) => {
  const data = block
    .split('\n')
    .filter((line) => line.startsWith('data:'))
    .map((line) => line.slice(5).trimStart())
    .join('\n');
  if (!data) return;
  connection.onmessage?.(new MessageEvent('message', { data }));
};

export const createSseConnection = (sessionId: string, afterEventId?: string | null): SseStreamConnection => {
  const token = readStoredAuthToken();
  const params = new URLSearchParams();
  if (afterEventId) params.set('afterEventId', afterEventId);
  const query = params.toString();
  const url = `${API_BASE_URL}/agent/sse/${sessionId}${query ? `?${query}` : ''}`;
  const controller = new AbortController();
  const connection: SseStreamConnection = {
    close: () => controller.abort(),
  };

  void (async () => {
    try {
      const headers: Record<string, string> = {};
      if (token) headers.Authorization = `Bearer ${token}`;

      const response = await fetch(url, {
        headers,
        signal: controller.signal,
        credentials: 'same-origin',
      });
      if (!response.ok || !response.body) {
        throw new Error(`SSE connection failed with status ${response.status}`);
      }

      connection.onopen?.();

      const reader = response.body
        .pipeThrough(new TextDecoderStream())
        .getReader();
      let buffer = '';

      while (true) {
        const { value, done } = await reader.read();
        if (done) break;

        buffer = (buffer + value).replace(/\r\n/g, '\n');
        let delimiter = buffer.indexOf('\n\n');
        while (delimiter >= 0) {
          const block = buffer.slice(0, delimiter);
          buffer = buffer.slice(delimiter + 2);
          dispatchSseBlock(connection, block);
          delimiter = buffer.indexOf('\n\n');
        }
      }
    } catch {
      if (!controller.signal.aborted) {
        connection.onerror?.();
      }
    }
  })();

  return connection;
};

// Novel Projects
export const createNovelProject = (req: NovelProjectCreateRequest) => {
  const payload = {
    title: req.title || '未命名新书',
    genre: req.genre,
    coreHook: req.seed,
  };
  return api<NovelProjectInfo>('/projects', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('novel-project', payload) },
    body: JSON.stringify(payload),
  });
};
export const activateNovelProject = (projectId: string) =>
  get<NovelProjectInfo>(`/projects/${projectId}`);
export const updateNovelProject = (projectId: string, req: NovelProjectUpdateRequest) =>
  api<NovelProjectInfo>(`/projects/${projectId}`, {
    method: 'PUT',
    body: JSON.stringify(req),
  });
export const deleteNovelProject = async (projectId: string): Promise<NovelProjectDeleteResult> => {
  await api<void>(`/projects/${projectId}`, { method: 'DELETE' });
  return { success: true, message: '项目已删除。', activeProjectId: '' };
};

export const listProjectChapters = (projectId: string) =>
  get<ChapterResponse[]>(`/chapters/project/${encodeURIComponent(projectId)}`);

export const createChapter = (req: ChapterCreateRequest) =>
  api<ChapterResponse>('/chapters', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('chapter', req) },
    body: JSON.stringify(req),
  });

export const getChapterById = (chapterId: string) =>
  get<ChapterResponse>(`/chapters/${encodeURIComponent(chapterId)}`);

export const getChapterVersions = (chapterId: string) =>
  get<ChapterVersionResponse[]>(`/chapters/${encodeURIComponent(chapterId)}/versions`);

export const compareChapterVersions = (
  chapterId: string,
  leftVersionId: string,
  rightVersionId: string,
) => {
  const query = new URLSearchParams({
    leftVersionId,
    rightVersionId,
  });
  return get<ChapterVersionCompareResponse>(
    `/chapters/${encodeURIComponent(chapterId)}/versions/compare?${query.toString()}`,
  );
};

export const rollbackChapterVersion = (chapterId: string, versionId: string, reason: string) =>
  api<{
    success: boolean;
    message: string;
    projectId: string;
    chapterId: string;
    currentVersionId: string;
    currentVersionNumber: number;
    currentDocumentId: string;
    runtimeRunId: string;
    invalidatedPackageIds: string[];
  }>(`/chapters/${encodeURIComponent(chapterId)}/versions/${encodeURIComponent(versionId)}/rollback`, {
    method: 'POST',
    headers: { 'Idempotency-Key': buildActionIdempotencyKey('chapter-version-rollback') },
    body: JSON.stringify({ reason }),
  });

// Settings
export const getSettings = () => get<UserSettings>('/settings');
export const getLlmConnectionHealth = () => get<LlmConnectionHealth>('/settings/llm-health');
export const saveSettings = (settings: Partial<UserSettings>) =>
  api<UserSettings>('/settings', {
    method: 'PUT',
    body: JSON.stringify(settings),
  });
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
