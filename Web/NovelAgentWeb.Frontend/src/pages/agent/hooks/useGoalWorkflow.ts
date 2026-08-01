import { useCallback, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  acceptGoalChapter,
  cancelGoal,
  changeGoalExecutionStrategy,
  continueGoalBatch,
  getGoalChapter,
  getGoalWorkflowStatus,
  mergeGoalPrefix,
  pauseGoal,
  resumeGoal,
  reworkGoalChapter,
  saveGoalChapterManualEdit,
} from '../../../api';
import type {
  GoalCancellationStrategy,
  GoalChapterReworkRequest,
  GoalChapterSummaryView,
  GoalChapterManualEditRequest,
} from '../../../api/types';
import { useGoalProgressStream } from './useGoalProgressStream';

function latestCandidates(candidates: GoalChapterSummaryView[]) {
  const byChapter = new Map<number, GoalChapterSummaryView>();
  for (const candidate of candidates) {
    const current = byChapter.get(candidate.chapterNumber);
    if (!current || candidate.version > current.version) {
      byChapter.set(candidate.chapterNumber, candidate);
    }
  }
  return [...byChapter.values()].sort((left, right) => left.chapterNumber - right.chapterNumber);
}

export function useGoalWorkflow(goalId: string) {
  const queryClient = useQueryClient();
  const [requestedChapterNumber, setRequestedChapterNumber] = useState<number | null>(null);
  const statusQuery = useQuery({
    queryKey: ['goal-workflow', goalId],
    queryFn: () => getGoalWorkflowStatus(goalId),
    enabled: Boolean(goalId),
    refetchInterval: 30_000,
  });
  const candidates = useMemo(
    () => latestCandidates(statusQuery.data?.candidates ?? []),
    [statusQuery.data?.candidates],
  );

  const selectedChapterNumber = candidates.some(
    (candidate) => candidate.chapterNumber === requestedChapterNumber,
  )
    ? requestedChapterNumber
    : candidates[0]?.chapterNumber ?? null;

  const chapterQuery = useQuery({
    queryKey: ['goal-chapter', goalId, selectedChapterNumber],
    queryFn: () => getGoalChapter(goalId, selectedChapterNumber!),
    enabled: Boolean(goalId) && selectedChapterNumber !== null,
  });

  const invalidate = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['goal-workflow', goalId] }),
      queryClient.invalidateQueries({ queryKey: ['goal-chapter', goalId] }),
    ]);
  }, [goalId, queryClient]);

  const sourceSessionId = statusQuery.data?.goal.sourceSessionId ?? null;
  useGoalProgressStream({ goalId, sessionId: sourceSessionId, onGoalEvent: invalidate });

  const reworkMutation = useMutation({
    mutationFn: ({ chapterNumber, request }: { chapterNumber: number; request: GoalChapterReworkRequest }) =>
      reworkGoalChapter(goalId, chapterNumber, request),
    onSuccess: invalidate,
  });
  const acceptMutation = useMutation({
    mutationFn: (candidate: GoalChapterSummaryView) =>
      acceptGoalChapter(goalId, candidate.chapterNumber, candidate.id, candidate.version),
    onSuccess: invalidate,
  });
  const manualEditMutation = useMutation({
    mutationFn: ({ chapterNumber, request }: { chapterNumber: number; request: GoalChapterManualEditRequest }) =>
      saveGoalChapterManualEdit(goalId, chapterNumber, request),
    onSuccess: invalidate,
  });
  const mergeMutation = useMutation({
    mutationFn: (branchId: string) => mergeGoalPrefix(goalId, branchId),
    onSuccess: invalidate,
  });
  const pauseMutation = useMutation({ mutationFn: () => pauseGoal(goalId), onSuccess: invalidate });
  const resumeMutation = useMutation({ mutationFn: () => resumeGoal(goalId), onSuccess: invalidate });
  const cancelMutation = useMutation({
    mutationFn: (strategy: GoalCancellationStrategy) => cancelGoal(goalId, strategy),
    onSuccess: invalidate,
  });
  const strategyMutation = useMutation({
    mutationFn: (executionStrategy: 'full_auto' | 'interactive_batch') =>
      changeGoalExecutionStrategy(goalId, executionStrategy),
    onSuccess: invalidate,
  });
  const continueBatchMutation = useMutation({
    mutationFn: () => continueGoalBatch(goalId),
    onSuccess: invalidate,
  });

  return {
    statusQuery,
    chapterQuery,
    candidates,
    selectedChapterNumber,
    sourceSessionId,
    setSelectedChapterNumber: setRequestedChapterNumber,
    reworkGoalChapter: reworkMutation.mutateAsync,
    acceptGoalChapter: acceptMutation.mutateAsync,
    saveGoalChapterManualEdit: manualEditMutation.mutateAsync,
    mergeGoalPrefix: mergeMutation.mutateAsync,
    pauseGoal: pauseMutation.mutateAsync,
    resumeGoal: resumeMutation.mutateAsync,
    cancelGoal: cancelMutation.mutateAsync,
    changeExecutionStrategy: strategyMutation.mutateAsync,
    continueBatch: continueBatchMutation.mutateAsync,
    isMutating:
      reworkMutation.isPending ||
      manualEditMutation.isPending ||
      acceptMutation.isPending ||
      mergeMutation.isPending ||
      pauseMutation.isPending ||
      resumeMutation.isPending ||
      cancelMutation.isPending ||
      strategyMutation.isPending ||
      continueBatchMutation.isPending,
    mutationError:
      reworkMutation.error ||
      manualEditMutation.error ||
      acceptMutation.error ||
      mergeMutation.error ||
      pauseMutation.error ||
      resumeMutation.error ||
      cancelMutation.error ||
      strategyMutation.error ||
      continueBatchMutation.error,
  };
}
