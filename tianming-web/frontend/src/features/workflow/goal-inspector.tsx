import { useState } from 'react';
import type {
  BookExecutionStrategy,
  GoalCancellationStrategy,
  GoalWorkflowStatusView,
} from '@/api/types';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

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

export function GoalInspector({
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
  if (!workflow) {
    return <aside className="rounded-xl border bg-card p-4 text-sm text-muted-foreground">加载 Goal...</aside>;
  }
  const activeBranch = workflow.branches.find((branch) => branch.status === 'active');
  const runningTask = workflow.tasks.find((task) => task.status === 'running')
    ?? workflow.tasks.find((task) => task.status === 'ready' || task.status === 'queued');
  const totalCostLimit = workflow.goal.totalCostLimit;
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
    <aside className="space-y-3 rounded-xl border bg-card p-4">
      <header className="flex items-center justify-between">
        <span className="font-mono text-[10px] tracking-wide text-muted-foreground uppercase">Creative Goal</span>
        <strong className="rounded-full bg-muted px-2 py-0.5 text-[11px]">{workflow.goal.status}</strong>
      </header>
      <h2 className="font-serif text-sm font-semibold">{workflow.goal.humanReadableObjective}</h2>

      {production && (
        <section className="space-y-2 rounded-lg bg-muted/50 p-3">
          <div className="flex items-baseline justify-between text-xs">
            <span className="text-muted-foreground">整书生产 · {production.status}</span>
            <strong>{completedChapters} / {totalChapters} 章</strong>
          </div>
          <small className="block text-[11px] text-muted-foreground">
            第 {currentBatch?.batchNumber ?? production.currentBatchNumber} 批
            {currentBatch
              ? ` · ${currentBatch.startChapterNumber}-${currentBatch.endChapterNumber} 章 · ${currentBatch.acceptanceActor} 验收`
              : ''}
          </small>
          <Select
            value={production.executionStrategy}
            disabled={disabled || runningTask?.status === 'running'}
            onValueChange={(value) => void changeExecutionStrategy(value as BookExecutionStrategy)}
          >
            <SelectTrigger className="h-8 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="full_auto">整书自动推进</SelectItem>
              <SelectItem value="interactive_batch">分批交互推进</SelectItem>
            </SelectContent>
          </Select>
          {production.executionStrategy === 'interactive_batch' && production.status === 'awaiting_user' && (
            <Button size="sm" className="w-full" onClick={() => void continueBatch()} disabled={disabled}>
              继续下一批
            </Button>
          )}
        </section>
      )}

      <dl className="grid grid-cols-3 gap-2 text-center text-xs">
        {[
          { label: '实际', value: money(workflow.goal.actualCost) },
          { label: '预留', value: money(workflow.goal.reservedCost) },
          { label: '上限', value: money(totalCostLimit) },
        ].map((item) => (
          <div key={item.label} className="rounded-lg bg-muted/50 px-2 py-1.5">
            <dt className="text-[10px] text-muted-foreground">{item.label}</dt>
            <dd className="mt-0.5 font-mono text-[11px]">{item.value}</dd>
          </div>
        ))}
      </dl>

      <section className="rounded-lg bg-muted/50 p-3 text-xs">
        <span className="text-muted-foreground">当前内核</span>
        <strong className="mt-0.5 block text-sm">{runningTask?.kernelName ?? '等待调度'}</strong>
        <small className="text-[11px] text-muted-foreground">{runningTask?.taskType ?? '任务图没有可执行节点'}</small>
      </section>

      <div className="grid grid-cols-3 gap-2">
        <Button variant="outline" size="sm" onClick={() => void pauseGoal()} disabled={disabled || !canPause}>
          暂停
        </Button>
        <Button variant="outline" size="sm" onClick={() => void resumeGoal()} disabled={disabled || !canResume}>
          恢复
        </Button>
        <Button
          variant="outline"
          size="sm"
          onClick={() => activeBranch && void mergeGoalPrefix(activeBranch.id)}
          disabled={disabled || !activeBranch || !canCancel}
        >
          合并前缀
        </Button>
      </div>

      <div className="space-y-2">
        <Select
          value={strategy}
          disabled={disabled || !canCancel}
          onValueChange={(value) => setStrategy(value as GoalCancellationStrategy)}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="PreserveCandidateBranch">保留候选分支</SelectItem>
            <SelectItem value="MergeAcceptedPrefix">合并已接受前缀</SelectItem>
            <SelectItem value="DiscardCandidateBranch">放弃候选分支</SelectItem>
          </SelectContent>
        </Select>
        <Button
          variant="destructive"
          size="sm"
          className="w-full"
          onClick={() => void cancelGoal(strategy)}
          disabled={disabled || !canCancel}
        >
          终止 Goal
        </Button>
      </div>

      <details>
        <summary className="flex cursor-pointer items-center justify-between text-xs">
          <strong>任务图</strong>
          <span className="text-muted-foreground">v{workflow.graph?.version ?? '-'}</span>
        </summary>
        <div className="mt-2 space-y-1">
          {workflow.tasks.map((task) => (
            <div key={task.id} className="flex items-center gap-2 text-[11px]">
              <span
                className={`inline-block size-1.5 rounded-full ${
                  task.status === 'running'
                    ? 'animate-pulse bg-primary'
                    : task.status === 'completed'
                      ? 'bg-emerald-500'
                      : task.status === 'failed'
                        ? 'bg-destructive'
                        : 'bg-muted-foreground/40'
                }`}
              />
              <span className="min-w-0 flex-1 truncate">
                <strong>{task.taskType}</strong>
                <small className="ml-1 text-muted-foreground">{task.kernelName}</small>
              </span>
              <i className="text-muted-foreground not-italic">{task.status}</i>
            </div>
          ))}
        </div>
      </details>
    </aside>
  );
}
