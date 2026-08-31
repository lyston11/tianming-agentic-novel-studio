import { api, get, post } from './client';
import type {
  AccessibleProjectCatalogItem,
  ActivateNovelAgentProjectContextRequest,
  AppendNovelAgentTurnRequest,
  ConfirmNovelAgentProposalRequest,
  ConfirmNovelAgentProposalResult,
  CreateLegacyRecoveryProposalRequest,
  CreateLegacyRecoveryProposalResult,
  NovelAgentConversationTurnResult,
  NovelAgentWorkflowResponse,
  ProjectContextActivationResult,
} from './types';

export const appendNovelAgentTurn = (
  sessionId: string,
  request: AppendNovelAgentTurnRequest,
) => api<NovelAgentConversationTurnResult>(
  `/novel-agent/conversations/${encodeURIComponent(sessionId)}/turns`,
  { method: 'POST', body: JSON.stringify(request) },
);

export const listAccessibleNovelAgentProjects = () =>
  get<AccessibleProjectCatalogItem[]>('/novel-agent/projects/accessible');

export const activateNovelAgentProjectContext = (
  sessionId: string,
  request: ActivateNovelAgentProjectContextRequest,
) => api<ProjectContextActivationResult>(
  `/novel-agent/conversations/${encodeURIComponent(sessionId)}/project-context/activate`,
  {
    method: 'POST',
    headers: { 'Idempotency-Key': request.idempotencyKey },
    body: JSON.stringify({
      projectId: request.projectId,
      expectedBindingVersion: request.expectedBindingVersion,
      sourceUserMessageId: request.sourceUserMessageId,
      confirmationActionId: request.confirmationActionId,
    }),
  },
);

export const confirmNovelAgentProposal = (
  proposalId: string,
  request: ConfirmNovelAgentProposalRequest,
) => api<ConfirmNovelAgentProposalResult>(
  `/novel-agent/proposals/${encodeURIComponent(proposalId)}/confirm`,
  { method: 'POST', body: JSON.stringify(request) },
);

export const getNovelAgentWorkflow = (projectId: string) =>
  get<NovelAgentWorkflowResponse>(`/novel-agent/workflows/projects/${encodeURIComponent(projectId)}`);

export const createLegacyRecoveryProposal = (
  projectId: string,
  request: CreateLegacyRecoveryProposalRequest,
) => api<CreateLegacyRecoveryProposalResult>(
  `/novel-agent/legacy/projects/${encodeURIComponent(projectId)}/recovery-proposals`,
  { method: 'POST', body: JSON.stringify(request) },
);

export const startNovelAgentProduction = (productionId: string) =>
  post(`/novel-agent/productions/${encodeURIComponent(productionId)}/commands/start`);

export const pauseNovelAgentProduction = (productionId: string, hasRunningTask: boolean) =>
  post(`/novel-agent/productions/${encodeURIComponent(productionId)}/commands/pause`, { hasRunningTask });

export const resumeNovelAgentProduction = (productionId: string) =>
  post(`/novel-agent/productions/${encodeURIComponent(productionId)}/commands/resume`);

export const cancelNovelAgentProduction = (productionId: string, reason: string) =>
  post(`/novel-agent/productions/${encodeURIComponent(productionId)}/commands/cancel`, { reason });

export const acceptNovelAgentPrefix = (
  productionId: string,
  request: { branchId: string; acceptedThroughChapter: number; idempotencyKey: string; correlationId: string },
) => post(`/novel-agent/productions/${encodeURIComponent(productionId)}/commands/accept-prefix`, request);
