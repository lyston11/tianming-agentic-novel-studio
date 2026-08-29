import type { GoalWorkflowStatusView } from '@/api/types';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

interface BookProductionStatusCardProps {
  workflow: GoalWorkflowStatusView;
  context?: 'chat' | 'workflow';
  rationale?: string;
  onOpenWorkflow?: () => void;
}

const STATUS_LABELS: Record<string, string> = {
  committed: '合同已确认',
  running: '正在生产',
  pause_requested: '正在安全暂停',
  paused: '已暂停',
  awaiting_user: '等待用户验收',
  awaiting_next_batch: '等待下一批确认',
  awaiting_decision: '等待处理',
  blocked: '生产受阻',
  completed: '整书完成',
  canceled: '已终止',
  budget_exceeded: '预算已用尽',
};

function statusLabel(status?: string | null) {
  if (!status) return '准备生产';
  return STATUS_LABELS[status] ?? status;
}

function actionLabel(status?: string | null) {
  if (status === 'awaiting_user' || status === 'awaiting_next_batch') return '进入工作流验收';
  if (status === 'blocked' || status === 'awaiting_decision' || status === 'budget_exceeded') return '进入工作流处理';
  if (status === 'completed') return '查看完本报告';
  return '查看实时工作流';
}

export function BookProductionStatusCard({
  workflow,
  context = 'chat',
  rationale,
  onOpenWorkflow,
}: BookProductionStatusCardProps) {
  const production = workflow.production;
  const currentBatch = workflow.batches.find((batch) => batch.batchNumber === production?.currentBatchNumber)
    ?? workflow.batches.find((batch) => batch.status === 'running' || batch.status === 'awaiting_user')
    ?? null;
  const completedChapters = workflow.batches
    .filter((batch) => batch.status === 'completed')
    .reduce((count, batch) => count + batch.endChapterNumber - batch.startChapterNumber + 1, 0);
  const totalChapters = production
    ? production.targetEndChapterNumber - production.targetStartChapterNumber + 1
    : workflow.candidateChapterCount;
  const progress = totalChapters > 0 ? Math.min(100, Math.round((completedChapters / totalChapters) * 100)) : 0;
  const currentTask = workflow.tasks.find((task) => task.status === 'running')
    ?? workflow.tasks.find((task) => task.status === 'ready' || task.status === 'queued')
    ?? null;
  const effectiveStatus = production?.status || workflow.goal.status;
  const isAttention = ['awaiting_user', 'awaiting_next_batch', 'awaiting_decision', 'blocked', 'budget_exceeded']
    .includes(effectiveStatus);

  return (
    <section
      className={cn(
        'rounded-xl border bg-card p-4',
        isAttention && 'border-primary/40 shadow-[0_0_0_1px_rgba(184,59,47,.12)]',
      )}
    >
      <header className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <span className="block font-mono text-[10px] tracking-wide text-muted-foreground uppercase">
            {context === 'chat' ? 'Agent 生产决策' : '整书生产状态'}
          </span>
          <strong className="block truncate font-serif text-sm">{workflow.goal.humanReadableObjective}</strong>
        </div>
        <em
          className={cn(
            'shrink-0 rounded-full px-2 py-0.5 text-[11px] not-italic',
            isAttention ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground',
          )}
        >
          {statusLabel(effectiveStatus)}
        </em>
      </header>

      <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
        {[
          {
            label: '执行策略',
            value: production?.executionStrategy === 'full_auto' ? '整书自动推进' : '分批交互推进',
          },
          {
            label: '当前批次',
            value: currentBatch
              ? `第 ${currentBatch.batchNumber} 批 · ${currentBatch.startChapterNumber}-${currentBatch.endChapterNumber} 章`
              : '等待批次计划',
          },
          {
            label: '验收主体',
            value: currentBatch?.acceptanceActor === 'agent' ? 'Agent 验收并留痕' : '用户在工作流验收',
          },
          {
            label: '当前动作',
            value: currentTask ? `${currentTask.kernelName} · ${currentTask.taskType}` : statusLabel(effectiveStatus),
          },
        ].map((item) => (
          <div key={item.label} className="rounded-lg bg-muted/50 px-2.5 py-2">
            <span className="block text-[10px] text-muted-foreground">{item.label}</span>
            <strong className="block truncate text-xs">{item.value}</strong>
          </div>
        ))}
      </div>

      <div className="mt-3 flex items-center gap-3">
        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted">
          <div className="h-full rounded-full bg-primary/70" style={{ width: `${progress}%` }} />
        </div>
        <strong className="text-xs">{completedChapters} / {totalChapters || '-'} 章</strong>
      </div>

      {rationale && <p className="mt-2 text-xs text-muted-foreground">{rationale}</p>}

      {onOpenWorkflow && (
        <footer className="mt-3 flex items-center justify-between border-t pt-3">
          <span className="text-[11px] text-muted-foreground">所有正文检查、返工和验收均以项目工作流为准。</span>
          <Button size="sm" onClick={onOpenWorkflow}>
            {actionLabel(effectiveStatus)}
          </Button>
        </footer>
      )}
    </section>
  );
}
