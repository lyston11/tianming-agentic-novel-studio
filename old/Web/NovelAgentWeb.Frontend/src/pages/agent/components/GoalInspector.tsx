import { useState } from 'react';
import type { BookExecutionStrategy, GoalCancellationStrategy, GoalWorkflowStatusView } from '../../../api/types';

interface GoalInspectorProps {
  workflow?: GoalWorkflowStatusView;
  disabled: boolean;
  pauseGoal: () => Promise<unknown>;
  resumeGoal: () => Promise<unknown>;
  cancelGoal: (strategy: GoalCancellationStrategy) => Promise<unknown>;
  mergeGoalPrefix: (branchId: string) => Promise<unknown>;
  changeExecutionStrategy: (strategy: BookExecutionStrategy) => Promise<unknown>;
  continueBatch: () => Promise<unknown>;
}

function money(value: number) {
  return `USD ${Number(value || 0).toFixed(4)}`;
}

export default function GoalInspector({
  workflow,
  disabled,
  pauseGoal,
  resumeGoal,
  cancelGoal,
  mergeGoalPrefix,
  changeExecutionStrategy,
  continueBatch,
}: GoalInspectorProps) {
  const [strategy, setStrategy] = useState<GoalCancellationStrategy>('PreserveCandidateBranch');
  if (!workflow) return <aside className="goal-inspector goal-loading">加载 Goal...</aside>;
  const activeBranch = workflow.branches.find((branch) => branch.status === 'active');
  const runningTask = workflow.tasks.find((task) => task.status === 'running')
    ?? workflow.tasks.find((task) => task.status === 'ready' || task.status === 'queued');
  const TotalCostLimit = workflow.goal.totalCostLimit;
  const goalStatus = workflow.goal.status;
  const canPause = ['committed', 'running', 'resumed', 'pause_requested'].includes(goalStatus);
  const canResume = goalStatus === 'paused';
  const canCancel = !['completed', 'canceled', 'budget_exceeded'].includes(goalStatus);
  const production = workflow.production;
  const currentBatch = workflow.batches.find((batch) => batch.batchNumber === production?.currentBatchNumber);
  const completedChapters = workflow.batches
    .filter((batch) => batch.status === 'completed')
    .reduce((count, batch) => count + batch.endChapterNumber - batch.startChapterNumber + 1, 0);
  const totalChapters = production
    ? production.targetEndChapterNumber - production.targetStartChapterNumber + 1
    : 0;

  return (
    <aside className="goal-inspector">
      <header>
        <span>Creative Goal</span>
        <strong>{workflow.goal.status}</strong>
      </header>
      <h2>{workflow.goal.humanReadableObjective}</h2>
      {production && (
        <section className="goal-current-task">
          <span>整书生产 · {production.status}</span>
          <strong>{completedChapters} / {totalChapters} 章</strong>
          <small>
            第 {currentBatch?.batchNumber ?? production.currentBatchNumber} 批
            {currentBatch ? ` · ${currentBatch.startChapterNumber}-${currentBatch.endChapterNumber} 章 · ${currentBatch.acceptanceActor} 验收` : ''}
          </small>
          <select
            value={production.executionStrategy}
            disabled={disabled || runningTask?.status === 'running'}
            onChange={(event) => void changeExecutionStrategy(event.target.value as BookExecutionStrategy)}
          >
            <option value="full_auto">整书自动推进</option>
            <option value="interactive_batch">分批交互推进</option>
          </select>
          {production.executionStrategy === 'interactive_batch' && production.status === 'awaiting_user' && (
            <button type="button" onClick={() => void continueBatch()} disabled={disabled}>继续下一批</button>
          )}
        </section>
      )}
      <dl className="goal-cost-grid">
        <div><dt>实际</dt><dd>{money(workflow.goal.actualCost)}</dd></div>
        <div><dt>预留</dt><dd>{money(workflow.goal.reservedCost)}</dd></div>
        <div><dt>上限</dt><dd>{money(TotalCostLimit)}</dd></div>
      </dl>
      <section className="goal-current-task">
        <span>当前内核</span>
        <strong>{runningTask?.kernelName ?? '等待调度'}</strong>
        <small>{runningTask?.taskType ?? '任务图没有可执行节点'}</small>
      </section>
      <div className="goal-control-row">
        <button type="button" onClick={() => void pauseGoal()} disabled={disabled || !canPause}>暂停</button>
        <button type="button" onClick={() => void resumeGoal()} disabled={disabled || !canResume}>恢复</button>
        <button
          type="button"
          onClick={() => activeBranch && void mergeGoalPrefix(activeBranch.id)}
          disabled={disabled || !activeBranch || !canCancel}
        >
          合并前缀
        </button>
      </div>
      <div className="goal-cancel-control">
        <select
          value={strategy}
          disabled={disabled || !canCancel}
          onChange={(event) => setStrategy(event.target.value as GoalCancellationStrategy)}
        >
          <option value="PreserveCandidateBranch">保留候选分支</option>
          <option value="MergeAcceptedPrefix">合并已接受前缀</option>
          <option value="DiscardCandidateBranch">放弃候选分支</option>
        </select>
        <button type="button" onClick={() => void cancelGoal(strategy)} disabled={disabled || !canCancel}>终止 Goal</button>
      </div>
      <details className="goal-task-list">
        <summary><strong>任务图</strong><span>v{workflow.graph?.version ?? '-'}</span></summary>
        <div className="goal-task-items">
          {workflow.tasks.map((task) => (
            <div key={task.id}>
              <span className={`goal-task-state ${task.status}`} />
              <span><strong>{task.taskType}</strong><small>{task.kernelName}</small></span>
              <i>{task.status}</i>
            </div>
          ))}
        </div>
      </details>
    </aside>
  );
}
