import { api, buildStableIdempotencyKey, get, post } from './client';
import type {
  DirectorTurnView,
  GoalCancellationStrategy,
  GoalChapterDetailView,
  GoalChapterManualEditRequest,
  GoalChapterManualEditResponse,
  GoalChapterReworkRequest,
  GoalChapterReworkResponse,
  GoalWorkflowConfirmationView,
  GoalWorkflowStatusView,
} from './types';

export const confirmGoalWorkflow = (
  director: DirectorTurnView,
  sessionId: string,
  totalCostLimit: number,
) => {
  if (!director.proposedContract) {
    throw new Error('当前导演回复没有可确认的 Goal 合同');
  }
  const command = {
    projectId: director.projectId,
    sourceSessionId: sessionId,
    idempotencyKey: buildStableIdempotencyKey('creative-goal', {
      projectId: director.projectId,
      sessionId,
      totalCostLimit,
      contract: director.proposedContract,
    }),
    totalCostLimit,
    contract: director.proposedContract,
  };
  return post<GoalWorkflowConfirmationView>('/goals/workflow/confirm', {
    assessment: {
      projectId: director.projectId,
      collaborationMode: director.proposedContract.collaborationMode,
      dialogue: [],
      acceptedDecisionsJson: '[]',
      projectStateJson: '{}',
      explicitExecutionAction: true,
      proposedContract: director.proposedContract,
    },
    command,
  });
};

export const getGoalWorkflowStatus = (goalId: string) =>
  get<GoalWorkflowStatusView>(`/goals/${encodeURIComponent(goalId)}/workflow`);

export const getLatestProjectGoalWorkflowStatus = (projectId: string) =>
  get<GoalWorkflowStatusView | null>(
    `/goals/project/${encodeURIComponent(projectId)}/latest/workflow`,
  );

export const getGoalChapter = (goalId: string, chapterNumber: number) =>
  get<GoalChapterDetailView>(
    `/goals/${encodeURIComponent(goalId)}/workflow/chapters/${chapterNumber}`,
  );

export const reworkGoalChapter = (
  goalId: string,
  chapterNumber: number,
  request: GoalChapterReworkRequest,
) => api<GoalChapterReworkResponse>(
  `/goals/${encodeURIComponent(goalId)}/workflow/chapters/${chapterNumber}/rework`,
  { method: 'POST', body: JSON.stringify(request) },
);

export const saveGoalChapterManualEdit = (
  goalId: string,
  chapterNumber: number,
  request: GoalChapterManualEditRequest,
) => post<GoalChapterManualEditResponse>(
  `/goals/${encodeURIComponent(goalId)}/workflow/chapters/${chapterNumber}/manual-edit`,
  request,
);

export const acceptGoalChapter = (
  goalId: string,
  chapterNumber: number,
  candidateChapterId: string,
  candidateVersion: number,
) => post(
  `/goals/${encodeURIComponent(goalId)}/workflow/chapters/${chapterNumber}/accept`,
  { candidateChapterId, candidateVersion },
);

export const mergeGoalPrefix = (goalId: string, branchId: string) =>
  post(`/goals/${encodeURIComponent(goalId)}/workflow/merge-prefix`, { branchId });

export const pauseGoal = (goalId: string) =>
  post(`/goals/${encodeURIComponent(goalId)}/workflow/pause`);

export const resumeGoal = (goalId: string) =>
  post(`/goals/${encodeURIComponent(goalId)}/workflow/resume`);

export const cancelGoal = (goalId: string, strategy: GoalCancellationStrategy) =>
  post(`/goals/${encodeURIComponent(goalId)}/workflow/cancel`, { strategy });

export const changeGoalExecutionStrategy = (goalId: string, executionStrategy: 'full_auto' | 'interactive_batch') =>
  post(`/goals/${encodeURIComponent(goalId)}/workflow/strategy`, { executionStrategy });

export const continueGoalBatch = (goalId: string) =>
  post(`/goals/${encodeURIComponent(goalId)}/workflow/continue-batch`);
