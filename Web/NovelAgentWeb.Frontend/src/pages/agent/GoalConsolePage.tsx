import { useSearchParams, useParams } from 'react-router-dom';
import Topbar from '../../components/layout/Topbar';
import BatchChapterNav from './components/BatchChapterNav';
import ChapterWorkspace from './components/ChapterWorkspace';
import GoalInspector from './components/GoalInspector';
import ReworkComposer from './components/ReworkComposer';
import { useGoalWorkflow } from './hooks/useGoalWorkflow';
import '../../styles/goal-console.css';

export default function GoalConsolePage() {
  const params = useParams<{ goalId?: string }>();
  const [search] = useSearchParams();
  const goalId = params.goalId?.trim() || search.get('goalId')?.trim() || '';
  const workflow = useGoalWorkflow(goalId);

  if (!goalId) {
    return (
      <div className="goal-console-page">
        <Topbar title="Goal 工作台" />
        <section className="goal-console-missing">当前没有已确认的 Creative Goal</section>
      </div>
    );
  }

  const detail = workflow.chapterQuery.data;
  const currentCandidate = workflow.candidates.find(
    (candidate) => candidate.chapterNumber === workflow.selectedChapterNumber,
  );
  const goalStatus = workflow.statusQuery.data?.goal.status;
  const candidateCommandsDisabled = workflow.isMutating
    || !currentCandidate
    || !workflow.sourceSessionId
    || currentCandidate.status !== 'candidate'
    || goalStatus === 'completed'
    || goalStatus === 'canceled'
    || goalStatus === 'budget_exceeded';

  return (
    <div className="goal-console-page">
      <Topbar
        title="Goal 工作台"
        center={<span>{workflow.statusQuery.data?.goal.humanReadableObjective ?? goalId}</span>}
      />
      {(workflow.statusQuery.error || workflow.chapterQuery.error || workflow.mutationError) && (
        <div className="goal-console-error">
          {String(workflow.statusQuery.error || workflow.chapterQuery.error || workflow.mutationError)}
        </div>
      )}
      <div className="goal-console-grid">
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
          workflow={workflow.statusQuery.data}
          disabled={workflow.isMutating}
          pauseGoal={workflow.pauseGoal}
          resumeGoal={workflow.resumeGoal}
          cancelGoal={workflow.cancelGoal}
          mergeGoalPrefix={workflow.mergeGoalPrefix}
        />
      </div>
    </div>
  );
}
