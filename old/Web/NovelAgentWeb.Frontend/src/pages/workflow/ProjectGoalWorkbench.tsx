import { useState } from 'react';
import BookProductionStatusCard from '../../components/production/BookProductionStatusCard';
import BatchChapterNav from '../agent/components/BatchChapterNav';
import ChapterWorkspace from '../agent/components/ChapterWorkspace';
import GoalInspector from '../agent/components/GoalInspector';
import ReworkComposer from '../agent/components/ReworkComposer';
import { useGoalWorkflow } from '../agent/hooks/useGoalWorkflow';
import '../../styles/goal-console.css';

interface ProjectGoalWorkbenchProps {
  goalId: string;
}

export default function ProjectGoalWorkbench({ goalId }: ProjectGoalWorkbenchProps) {
  const [expanded, setExpanded] = useState(true);
  const workflow = useGoalWorkflow(goalId);
  const status = workflow.statusQuery.data;
  const detail = workflow.chapterQuery.data;
  const currentCandidate = workflow.candidates.find(
    (candidate) => candidate.chapterNumber === workflow.selectedChapterNumber,
  );
  const goalStatus = status?.goal.status;
  const candidateCommandsDisabled = workflow.isMutating
    || !currentCandidate
    || !workflow.sourceSessionId
    || currentCandidate.status !== 'candidate'
    || goalStatus === 'completed'
    || goalStatus === 'canceled'
    || goalStatus === 'budget_exceeded';

  return (
    <section className="project-goal-workbench" aria-label="整书生产与验收">
      <div className="project-goal-workbench-heading">
        <div>
          <span>Book Production</span>
          <h3>整书生产与验收</h3>
          <p>批次、候选正文、审校证据和执行控制统一在项目工作流中处理。</p>
        </div>
        <button type="button" className="ghost-button compact" onClick={() => setExpanded((value) => !value)}>
          {expanded ? '收起验收台' : '展开验收台'}
        </button>
      </div>

      {status && <BookProductionStatusCard workflow={status} context="workflow" />}
      {(workflow.statusQuery.error || workflow.chapterQuery.error || workflow.mutationError) && (
        <div className="goal-console-error">
          {String(workflow.statusQuery.error || workflow.chapterQuery.error || workflow.mutationError)}
        </div>
      )}

      {expanded && (
        <div className="goal-console-grid project-goal-workbench-grid">
          <BatchChapterNav
            candidates={workflow.candidates}
            selectedChapterNumber={workflow.selectedChapterNumber}
            onSelect={workflow.setSelectedChapterNumber}
          />
          <div className="goal-console-center">
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
