import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { BookProductionStatusCard } from '@/features/agent/book-production-status-card';
import { useGoalWorkflow } from './use-goal-workflow';
import { BatchChapterNav } from './batch-chapter-nav';
import { ChapterWorkspace } from './chapter-workspace';
import { ReworkComposer } from './rework-composer';
import { GoalInspector } from './goal-inspector';

interface GoalWorkbenchProps {
  goalId: string;
}

/** Per-goal production & acceptance console. */
export function GoalWorkbench({ goalId }: GoalWorkbenchProps) {
  const navigate = useNavigate();
  const [expanded, setExpanded] = useState(true);
  const workflow = useGoalWorkflow(goalId);
  const status = workflow.statusQuery.data;
  const detail = workflow.chapterQuery.data;
  const currentCandidate = workflow.candidates.find(
    (candidate) => candidate.chapterNumber === workflow.selectedChapterNumber,
  );
  const goalStatus = status?.goal.status;
  const candidateCommandsDisabled =
    workflow.isMutating
    || !currentCandidate
    || !workflow.sourceSessionId
    || currentCandidate.status !== 'candidate'
    || goalStatus === 'completed'
    || goalStatus === 'canceled'
    || goalStatus === 'budget_exceeded';

  return (
    <section className="space-y-4" aria-label="整书生产与验收">
      <div className="flex items-start justify-between gap-3">
        <div>
          <span className="font-mono text-[10px] tracking-wide text-muted-foreground uppercase">Book Production</span>
          <h3 className="font-serif text-base font-bold">整书生产与验收</h3>
          <p className="text-xs text-muted-foreground">批次、候选正文、审校证据和执行控制统一在项目工作流中处理。</p>
        </div>
        <Button variant="ghost" size="sm" onClick={() => setExpanded((value) => !value)}>
          {expanded ? '收起验收台' : '展开验收台'}
        </Button>
      </div>

      {status && (
        <BookProductionStatusCard
          workflow={status}
          context="workflow"
          onOpenWorkflow={() => goalId && navigate('.')}
        />
      )}
      {(workflow.statusQuery.error || workflow.chapterQuery.error || workflow.mutationError) && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
          {String(workflow.statusQuery.error || workflow.chapterQuery.error || workflow.mutationError)}
        </div>
      )}

      {expanded && (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[240px_1fr_300px]">
          <BatchChapterNav
            candidates={workflow.candidates}
            selectedChapterNumber={workflow.selectedChapterNumber}
            onSelect={workflow.setSelectedChapterNumber}
          />
          <div className="min-w-0 space-y-4">
            <ChapterWorkspace
              key={detail?.candidate.id ?? 'empty'}
              detail={detail}
              loading={workflow.chapterQuery.isLoading}
              disabled={candidateCommandsDisabled}
              onAccept={() => currentCandidate && void workflow.acceptGoalChapter(currentCandidate)}
              saveManualEdit={workflow.saveGoalChapterManualEdit}
            />
            <ReworkComposer
              detail={detail}
              sessionId={workflow.sourceSessionId ?? ''}
              disabled={candidateCommandsDisabled}
              reworkGoalChapter={workflow.reworkGoalChapter}
            />
          </div>
          <GoalInspector
            workflow={status}
            disabled={workflow.isMutating}
            pauseGoal={workflow.pauseGoal}
            resumeGoal={workflow.resumeGoal}
            cancelGoal={workflow.cancelGoal}
            mergeGoalPrefix={workflow.mergeGoalPrefix}
            changeExecutionStrategy={workflow.changeExecutionStrategy}
            continueBatch={workflow.continueBatch}
          />
        </div>
      )}
    </section>
  );
}
