import CollapsePanel from './CollapsePanel';
import SettingsSelect from './SettingsSelect';
import type { UserSettings } from '../../api/types';

interface CreativeTabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
}

export default function CreativeTab({ form, update }: CreativeTabProps) {
  const riskOptions = [
    { value: 'Low', label: '低 - 自动执行所有步骤' },
    { value: 'Medium', label: '中 - 高风险步骤需确认' },
    { value: 'High', label: '高 - 所有步骤需确认' },
  ];
  const writingStyleOptions = [
    { value: 'balanced', label: '平衡（默认）' },
    { value: 'concise', label: '简洁' },
    { value: 'detailed', label: '详细' },
  ];
  const pacingOptions = [
    { value: 'medium', label: '中等（默认）' },
    { value: 'fast', label: '快节奏' },
    { value: 'slow', label: '慢节奏' },
  ];

  return (
    <div className="settings-tab-content">
      {/* 3.1 Agent Behavior */}
      <CollapsePanel id="creative-1" title="Agent 行为" defaultOpen={true}>
        <div className="form-grid">
          <p className="settings-desc">控制 Agent loop 的风险阈值和推进步数。</p>
          <div className="form-field">
            <label>默认风险等级</label>
            <SettingsSelect
              value={form.agentDefaultRisk || 'Medium'}
              options={riskOptions}
              onChange={(value) => update('agentDefaultRisk', value)}
            />
          </div>
          <div className="form-field">
            <label>
              <input
                type="checkbox"
                checked={form.agentLoopAutoProceed ?? true}
                onChange={(e) => update('agentLoopAutoProceed', e.target.checked)}
              />
              Agent loop 自动推进
            </label>
          </div>
          <div className="form-field">
            <label>最大循环步数</label>
            <input
              type="number"
              value={form.agentLoopMaxSteps ?? 12}
              min={1}
              max={100}
              onChange={(e) => update('agentLoopMaxSteps', Number(e.target.value))}
            />
          </div>
        </div>
      </CollapsePanel>

      {/* 3.2 Creative Defaults */}
      <CollapsePanel id="creative-2" title="创作默认值" defaultOpen={false}>
        <div className="form-grid">
          <p className="settings-desc">新建创作时的默认参数。</p>
          <div className="form-row">
            <div className="form-field">
              <label>默认类型</label>
              <input value={form.defaultGenre || ''} onChange={(e) => update('defaultGenre', e.target.value)} placeholder="玄幻" />
            </div>
            <div className="form-field">
              <label>默认子类型</label>
              <input value={form.defaultSubGenre || ''} onChange={(e) => update('defaultSubGenre', e.target.value)} />
            </div>
          </div>
          <div className="form-row">
            <div className="form-field">
              <label>默认章节字数</label>
              <input type="number" min={500} max={50000} value={form.defaultChapterWordCount ?? 3000} onChange={(e) => update('defaultChapterWordCount', Number(e.target.value))} />
            </div>
            <div className="form-field">
              <label>默认每卷章节数</label>
              <input type="number" min={1} max={200} value={form.defaultVolumeChapterCount ?? 6} onChange={(e) => update('defaultVolumeChapterCount', Number(e.target.value))} />
            </div>
          </div>
        </div>
      </CollapsePanel>

      {/* 3.3 Writing Preferences */}
      <CollapsePanel id="creative-3" title="写作偏好" defaultOpen={false}>
        <div className="form-grid">
          <p className="settings-desc">个性化写作风格和节奏控制。</p>
          <div className="form-field">
            <label>写作风格</label>
            <SettingsSelect value="balanced" options={writingStyleOptions} disabled />
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
          <div className="form-field">
            <label>节奏控制</label>
            <SettingsSelect value="medium" options={pacingOptions} disabled />
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
        </div>
      </CollapsePanel>
    </div>
  );
}
