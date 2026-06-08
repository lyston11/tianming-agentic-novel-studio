import type { NovelAgentPlanStep } from '../../api/types';
import { useAgentStore } from '../../stores/useAgentStore';
import { rollbackStep } from '../../api';
import { useQueryClient } from '@tanstack/react-query';

const RISK_LABELS: Record<string, string> = { Low: '低', Medium: '中', High: '高', Critical: '极高' };
const STATUS_LABELS: Record<string, string> = {
  Pending: '待执行', Running: '执行中', Completed: '已完成',
  Failed: '失败', Skipped: '跳过', WaitingUser: '等待确认',
};

interface Props {
  step: NovelAgentPlanStep;
  runId: string;
}

export default function StepDetail({ step, runId }: Props) {
  const { sessionId, setSelectedStep } = useAgentStore();
  const queryClient = useQueryClient();

  const handleRollback = async () => {
    if (!sessionId || !runId) return;
    try {
      await rollbackStep(sessionId, runId, step.id);
      queryClient.invalidateQueries({ queryKey: ['storyBible'] });
      queryClient.invalidateQueries({ queryKey: ['runs'] });
    } catch (err) {
      console.error('Rollback failed:', err);
    }
  };

  return (
    <div className="step-detail">
      <div className="step-detail-header">
        <h4>{step.name}</h4>
        <button className="ghost-button" style={{ fontSize: 11, padding: '2px 6px' }} onClick={() => setSelectedStep(null)}>
          关闭
        </button>
      </div>

      <dl>
        <dt>用途</dt>
        <dd>{step.purpose || '无'}</dd>
        <dt>工具</dt>
        <dd>{step.toolName || '无'}</dd>
        <dt>状态</dt>
        <dd>{STATUS_LABELS[step.status] ?? step.status}</dd>
        <dt>风险等级</dt>
        <dd>{RISK_LABELS[step.riskLevel] ?? step.riskLevel}</dd>
        <dt>需要确认</dt>
        <dd>{step.requiresConfirmation ? '是' : '否'}</dd>
      </dl>

      {Object.keys(step.inputs).length > 0 && (
        <>
          <h5>输入参数</h5>
          <pre className="step-inputs">{JSON.stringify(step.inputs, null, 2)}</pre>
        </>
      )}

      {(step.status === 'Completed' || step.status === 'Failed') && (
        <button className="ghost-button" style={{ marginTop: 12 }} onClick={handleRollback}>
          回退到此步骤重新执行
        </button>
      )}
    </div>
  );
}
