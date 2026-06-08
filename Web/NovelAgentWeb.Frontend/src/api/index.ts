import { api, get, post } from './client';
import type {
  WorkspaceInfo,
  StoryBibleDocument,
  NovelAgentRun,
  NovelAgentRunOperationResult,
  StoryFoundationRequest,
  CommitStoryFoundationRequest,
  StoryBibleCommitResult,
  VolumeArcPlanningRequest,
  ConfirmRequest,
  ChapterCreativeRequest,
  SelectChapterCandidateRequest,
  ChapterCandidateSelectionResult,
  ConfirmOnlyRequest,
  NovelAgentExecutionResult,
  ContinueRunRequest,
  CanonMaintenanceResult,
  ForeshadowMaintenanceResult,
  CharacterMaintenanceResult,
  EntryConfirmRequest,
  CreativeKnowledgeQueryRequest,
  CreativeKnowledgeRetrievalResult,
  CreativeKnowledgeEntry,
  CreativeKnowledgeMutationResult,
  UsedPatternRequest,
  MaterialLibraryDocument,
  MaterialIngestRequest,
  MaterialUpdateRequest,
  MaterialAnalysisRequest,
  MaterialAnalysisResult,
  AgentChatRequest,
  AgentChatResponse,
  AgentSessionInfo,
  AgentSessionSummary,
  AgentSessionUpdateRequest,
  NovelLibraryDocument,
  ProjectWorkflowDocument,
  NovelProjectCreateRequest,
  NovelProjectDeleteResult,
  NovelProjectInfo,
  NovelProjectUpdateRequest,
  UserSettings,
} from './types';

// Workspace
export const getWorkspace = () => get<WorkspaceInfo>('/workspace');

// Story Bible
export const getStoryBible = () => get<StoryBibleDocument>('/story-bible');

// Runs
export const listRuns = (take?: number) =>
  get<NovelAgentRunOperationResult>(`/runs${take ? `?take=${take}` : ''}`);
export const getRun = (runId: string) =>
  get<NovelAgentRunOperationResult>(`/run/${runId}`);
export const continueRun = (runId: string, req?: ContinueRunRequest) =>
  post<NovelAgentRunOperationResult>(`/run/${runId}/continue`, req ?? { maxAutoRisk: 'Medium', maxAutoSteps: 12 });
export const reviewRun = (runId: string) =>
  post<NovelAgentExecutionResult>(`/run/${runId}/review`);
export const rewriteRun = (runId: string, req?: ConfirmOnlyRequest) =>
  post<NovelAgentExecutionResult>(`/run/${runId}/rewrite`, req ?? { confirmed: true });

// Story Foundation
export const planFoundation = (req: StoryFoundationRequest) =>
  post<NovelAgentRun>('/story-foundation', req);
export const commitFoundation = (runId: string, req: CommitStoryFoundationRequest) =>
  post<StoryBibleCommitResult>(`/story-foundation/${runId}/commit`, req);

// Volume Arc
export const planVolume = (req: VolumeArcPlanningRequest) =>
  post<NovelAgentRun>('/volume-arc', req);
export const commitVolume = (runId: string, req?: ConfirmRequest) =>
  post<StoryBibleCommitResult>(`/volume-arc/${runId}/commit`, req ?? { overwrite: false, confirmed: true });

// Chapter
export const planChapter = (req: ChapterCreativeRequest) =>
  post<NovelAgentRun>('/chapter-plan', req);
export const selectCandidate = (runId: string, req: SelectChapterCandidateRequest) =>
  post<ChapterCandidateSelectionResult>(`/chapter-candidate/${runId}/select`, req);
export const executeChapter = (runId: string, req?: ConfirmOnlyRequest) =>
  post<NovelAgentExecutionResult>(`/chapter/${runId}/execute`, req ?? { confirmed: true });

// Ledger
export const importCanon = (runId: string) =>
  post<CanonMaintenanceResult>(`/run/${runId}/canon/import`);
export const promoteCanon = (runId: string, req: EntryConfirmRequest) =>
  post<CanonMaintenanceResult>(`/run/${runId}/canon/promote`, req);
export const importForeshadow = (runId: string) =>
  post<ForeshadowMaintenanceResult>(`/run/${runId}/foreshadow/import`);
export const confirmForeshadow = (runId: string, req: EntryConfirmRequest) =>
  post<ForeshadowMaintenanceResult>(`/run/${runId}/foreshadow/confirm`, req);
export const importCharacter = (runId: string) =>
  post<CharacterMaintenanceResult>(`/run/${runId}/character/import`);
export const confirmCharacter = (runId: string, req: EntryConfirmRequest) =>
  post<CharacterMaintenanceResult>(`/run/${runId}/character/confirm`, req);

// Creative Knowledge
export const searchKnowledge = (req: CreativeKnowledgeQueryRequest) =>
  post<CreativeKnowledgeRetrievalResult>('/creative-knowledge/search', req);
export const recordUsedPattern = (req: UsedPatternRequest) =>
  post('/creative-knowledge/used-pattern', req);

// Materials
export const getMaterials = () => get<MaterialLibraryDocument>('/materials');
export const ingestMaterial = (req: MaterialIngestRequest) =>
  post<MaterialLibraryDocument>('/materials', req);

export const uploadMaterialFile = async (file: File) => {
  const formData = new FormData();
  formData.append('file', file);
  const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';
  const response = await fetch(`${BASE_URL}/materials/upload`, {
    method: 'POST',
    body: formData,
  });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json() as Promise<{ materialId: string; fileName: string; characterCount: number }>;
};

export const analyzeMaterial = (req: MaterialAnalysisRequest) =>
  post<MaterialAnalysisResult>('/materials/analyze', req);

export const deleteMaterial = (materialId: string) =>
  api<void>(`/materials/${materialId}`, { method: 'DELETE' });

export const updateMaterial = (materialId: string, req: MaterialUpdateRequest) =>
  api<MaterialLibraryDocument>(`/materials/${materialId}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const getMaterialContent = (materialId: string) =>
  get<{ content: string }>(`/materials/${materialId}/content`);

export const getKnowledgeEntries = () =>
  get<CreativeKnowledgeEntry[]>('/creative-knowledge/entries');

export const deleteKnowledgeEntry = (entryId: string) =>
  api<CreativeKnowledgeMutationResult>(`/creative-knowledge/entries/${entryId}`, { method: 'DELETE' });

export const updateKnowledgeEntry = (entryId: string, req: CreativeKnowledgeEntry) =>
  api<CreativeKnowledgeMutationResult>(`/creative-knowledge/entries/${entryId}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

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

// Novel Library
export const getNovelLibrary = (projectId?: string) =>
  get<NovelLibraryDocument>(`/novel-library${projectId ? `?projectId=${encodeURIComponent(projectId)}` : ''}`);
export const getProjectWorkflow = (projectId: string) =>
  get<ProjectWorkflowDocument>(`/novel-projects/${encodeURIComponent(projectId)}/workflow`);
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
