import type { GoalWorkflowStatusView } from '../../api/types';
import '../../styles/book-production.css';

interface BookProductionStatusCardProps {
  workflow: GoalWorkflowStatusView;
  context?: 'chat' | 'workflow';
  rationale?: string;
  onOpenWorkflow?: () => void;
}

const statusLabels: Record<string, string> = {
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
  return statusLabels[status] ?? status;
}

function actionLabel(status?: string | null) {
  if (status === 'awaiting_user' || status === 'awaiting_next_batch') return '进入工作流验收';
  if (status === 'blocked' || status === 'awaiting_decision' || status === 'budget_exceeded') return '进入工作流处理';
  if (status === 'completed') return '查看完本报告';
  return '查看实时工作流';
}

export default function BookProductionStatusCard({
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
  const isAttention = ['awaiting_user', 'awaiting_next_batch', 'awaiting_decision', 'blocked', 'budget_exceeded'].includes(effectiveStatus);

  return (
    <section className={`book-production-status-card ${context} ${isAttention ? 'attention' : ''}`}>
      <header>
        <div>
          <span>{context === 'chat' ? 'Agent 生产决策' : '整书生产状态'}</span>
          <strong>{workflow.goal.humanReadableObjective}</strong>
        </div>
        <em>{statusLabel(effectiveStatus)}</em>
      </header>

      <div className="book-production-decision-grid">
        <div>
          <span>执行策略</span>
          <strong>{production?.executionStrategy === 'full_auto' ? '整书自动推进' : '分批交互推进'}</strong>
        </div>
        <div>
          <span>当前批次</span>
          <strong>
            {currentBatch
              ? `第 ${currentBatch.batchNumber} 批 · ${currentBatch.startChapterNumber}-${currentBatch.endChapterNumber} 章`
              : '等待批次计划'}
          </strong>
        </div>
        <div>
          <span>验收主体</span>
          <strong>{currentBatch?.acceptanceActor === 'agent' ? 'Agent 验收并留痕' : '用户在工作流验收'}</strong>
        </div>
        <div>
          <span>当前动作</span>
          <strong>{currentTask ? `${currentTask.kernelName} · ${currentTask.taskType}` : statusLabel(effectiveStatus)}</strong>
        </div>
      </div>

      <div className="book-production-progress-row">
        <div>
          <i style={{ width: `${progress}%` }} />
        </div>
        <strong>{completedChapters} / {totalChapters || '-'} 章</strong>
      </div>

      {rationale && <p className="book-production-rationale">{rationale}</p>}

      {onOpenWorkflow && (
        <footer>
          <span>所有正文检查、返工和验收均以项目工作流为准。</span>
          <button type="button" className="ink-button" onClick={onOpenWorkflow}>
            {actionLabel(effectiveStatus)}
          </button>
        </footer>
      )}
    </section>
  );
}
