import type { NovelAgentRun } from '../../api/types';
import { useAgentStore } from '../../stores/useAgentStore';

const STATUS_COLORS: Record<string, string> = {
  Pending: 'var(--muted)',
  Running: 'var(--gold)',
  Completed: 'var(--jade)',
  Failed: 'var(--red)',
  Skipped: 'var(--muted)',
  WaitingUser: 'var(--blue)',
};

const STATUS_LABELS: Record<string, string> = {
  Pending: '待执行',
  Running: '执行中',
  Completed: '已完成',
  Failed: '失败',
  Skipped: '跳过',
  WaitingUser: '等待确认',
};

const RISK_LABELS: Record<string, string> = {
  Low: '低',
  Medium: '中',
  High: '高',
  Critical: '极高',
};

interface Props {
  run: NovelAgentRun | null;
}

export default function ExecutionGraph({ run }: Props) {
  const { selectedStep, setSelectedStep } = useAgentStore();

  if (!run) {
    return (
      <div className="execution-graph">
        <div className="empty">等待 Agent 开始执行</div>
      </div>
    );
  }

  return (
    <div className="execution-graph">
      <div className="graph-header">
        <h3>执行步骤</h3>
        <span className="run-status">{run.status}</span>
      </div>
      <div className="graph-steps">
        {run.steps.map((step, i) => (
          <div
            key={step.id}
            className={`graph-step ${step.status.toLowerCase()}${selectedStep?.id === step.id ? ' selected' : ''}`}
            onClick={() => setSelectedStep(selectedStep?.id === step.id ? null : step)}
          >
            <div className="step-connector">
              <div className="step-dot" style={{ background: STATUS_COLORS[step.status] }} />
              {i < run.steps.length - 1 && <div className="step-line" />}
            </div>
            <div className="step-info">
              <div className="step-name">{step.name}</div>
              <div className="step-meta">
                <span className="step-status-badge" style={{ color: STATUS_COLORS[step.status] }}>
                  {STATUS_LABELS[step.status] ?? step.status}
                </span>
                {step.riskLevel !== 'Low' && (
                  <span className="step-risk">风险: {RISK_LABELS[step.riskLevel]}</span>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
