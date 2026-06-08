import type { MaterialAnalysisProgress } from '../../api/types';

const STAGE_ICONS: Record<string, string> = {
  genre: '书',
  world: '界',
  character: '人',
  plot: '剧',
  auto: '智',
};

interface Props {
  stages: MaterialAnalysisProgress[];
  currentStage: MaterialAnalysisProgress | null;
  isAnalyzing: boolean;
}

export default function AnalysisProgress({ stages, currentStage, isAnalyzing }: Props) {
  const allStages = [
    { key: 'genre', label: '题材风格分析' },
    { key: 'world', label: '世界观提取' },
    { key: 'character', label: '角色体系拆解' },
    { key: 'plot', label: '情节模式识别' },
    { key: 'auto', label: 'Agent 自动维度' },
  ];

  const getStageStatus = (key: string) => {
    const stage = stages.find((s) => s.stage === key);
    if (!stage) return currentStage?.stage === key && isAnalyzing ? 'running' : 'pending';
    return stage.status;
  };

  const getStageEntries = (key: string) => {
    const stage = stages.find((s) => s.stage === key);
    return stage?.entries?.length ?? 0;
  };

  if (!isAnalyzing && stages.length === 0) return null;

  return (
    <div className="analysis-panel">
      <h3 style={{ margin: '0 0 16px', color: 'var(--paper)', fontSize: 16 }}>
        {isAnalyzing ? '正在智能拆解...' : '拆解完成'}
      </h3>
      <div className="analysis-stepper">
        {allStages.map((s, i) => {
          const status = getStageStatus(s.key);
          const entries = getStageEntries(s.key);
          return (
            <div key={s.key} className={`analysis-step ${status}`}>
              <div className="step-indicator">
                <span className="step-icon">{STAGE_ICONS[s.key]}</span>
                <span className="step-num">{i + 1}</span>
              </div>
              <div className="step-content">
                <div className="step-label">{s.label}</div>
                <div className="step-status">
                  {status === 'running' && '分析中...'}
                  {status === 'completed' && `完成 · ${entries} 条知识`}
                  {status === 'failed' && '失败'}
                  {status === 'pending' && '等待中'}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
