/**
 * API layer barrel. Domain modules keep endpoint functions close to their
 * feature code; this barrel preserves the legacy flat import surface
 * (`import { getSettings } from '@/api'`).
 */
export {
  API_BASE_URL,
  ApiError,
  EnvelopeError,
  api,
  get,
  post,
  put,
  patch,
  del,
  buildStableIdempotencyKey,
  buildActionIdempotencyKey,
  createSseConnection,
  createNovelAgentConversationSseConnection,
  createNovelAgentWorkflowSseConnection,
  type ApiEnvelope,
  type SseStreamConnection,
} from './client';

export {
  AUTH_STORAGE_KEY,
  AUTH_UNAUTHORIZED_EVENT,
  clearStoredAuthSession,
  getStoredAuthSnapshot,
  hasValidAuthSession,
  notifyUnauthorizedSession,
  sanitizeAuthState,
  setAuthSession,
  subscribeToAuth,
  type AuthResponse,
  type AuthUser,
  type LoginRequest,
  type RegisterRequest,
  type StoredAuthState,
} from './auth-store';

export { authApi } from './auth';
export {
  activateNovelProject,
  createNovelProject,
  deleteNovelProject,
  getWorkspace,
  getUserStats,
  listProjects,
  toNovelProjectInfo,
  updateNovelProject,
  type ProjectResponse,
} from './projects';
export {
  createMaterialFromText,
  deleteMaterialById,
  getMaterialById,
  getMaterialContent,
  listMaterials,
  updateMaterialById,
  uploadMaterial,
} from './materials';
export {
  createKnowledgeDirectory,
  createKnowledgeEntry,
  deleteKnowledgeDirectory,
  deleteKnowledgeEntryById,
  getKnowledgeTask,
  listKnowledgeDirectories,
  listKnowledgeEntries,
  searchKnowledgeEntries,
  updateKnowledgeDirectory,
  updateKnowledgeEntryById,
  uploadKnowledgeFile,
} from './knowledge';
export {
  createCreativeIntent,
  decideCreativeIntent,
  listCreativeIntents,
} from './creative';
export {
  createCharacter,
  createOrUpdateConstitution,
  deleteCharacterById,
  getConstitution,
  getStoryBibleByProject,
  listCharacters,
  updateCharacterById,
} from './storybible';
export {
  createVolumeArc,
  deleteVolumeArc,
  getProjectWorkflow,
  getVolumeArc,
  listVolumeArcs,
  updateVolumeArc,
} from './volumes';
export {
  acceptGoalChapter,
  cancelGoal,
  changeGoalExecutionStrategy,
  confirmGoalWorkflow,
  continueGoalBatch,
  getGoalChapter,
  getGoalWorkflowStatus,
  getLatestProjectGoalWorkflowStatus,
  mergeGoalPrefix,
  pauseGoal,
  resumeGoal,
  reworkGoalChapter,
  saveGoalChapterManualEdit,
} from './goals';
export {
  acceptNovelAgentPrefix,
  activateNovelAgentProjectContext,
  appendNovelAgentTurn,
  cancelNovelAgentProduction,
  confirmNovelAgentProposal,
  createLegacyRecoveryProposal,
  getNovelAgentWorkflow,
  listAccessibleNovelAgentProjects,
  pauseNovelAgentProduction,
  resumeNovelAgentProduction,
  startNovelAgentProduction,
} from './novel-agent';
export {
  createAgentSession,
  getAgentSession,
  getSessionActiveRuntimeRun,
  listAgentSessions,
  listRuntimeEvents,
  resumeAgentSession,
  updateAgentSession,
} from './chat';
export {
  compareChapterVersions,
  createChapter,
  getChapterById,
  getChapterVersions,
  listProjectChapters,
  rollbackChapterVersion,
} from './chapters';
export {
  getLlmConnectionHealth,
  getSettings,
  resetSettings,
  saveSettings,
  testConnection,
} from './settings';
