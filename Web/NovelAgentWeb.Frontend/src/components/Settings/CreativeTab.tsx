import CollapsePanel from './CollapsePanel';
import type { UserSettings } from '../../api/types';

interface CreativeTabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
}

export default function CreativeTab({ form, update }: CreativeTabProps) {
  return (
    <div className="settings-tab-content">
      {/* 3.1 Agent Behavior */}
      <CollapsePanel id="creative-1" title="Agent 行为" defaultOpen={true}>
        <div className="form-grid">
          <p className="settings-desc">控制 Agent 自动执行的风险阈值和步数限制。</p>
          <div className="form-field">
            <label>默认风险等级</label>
            <select value={form.agentDefaultRisk || 'Medium'} onChange={(e) => update('agentDefaultRisk', e.target.value)}>
              <option value="Low">低 - 自动执行所有步骤</option>
              <option value="Medium">中 - 高风险步骤需确认</option>
              <option value="High">高 - 所有步骤需确认</option>
            </select>
          </div>
          <div className="form-field">
            <label>
              <input
                type="checkbox"
                checked={form.agentAutoContinue ?? true}
                onChange={(e) => update('agentAutoContinue', e.target.checked)}
              />
              自动继续执行（低风险步骤无需确认）
            </label>
          </div>
          <div className="form-field">
            <label>最大自动步数</label>
            <input
              type="number"
              value={form.agentMaxAutoSteps ?? 12}
              min={1}
              max={100}
              onChange={(e) => update('agentMaxAutoSteps', Number(e.target.value))}
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
            <select disabled>
              <option value="balanced">平衡（默认）</option>
              <option value="concise">简洁</option>
              <option value="detailed">详细</option>
            </select>
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
          <div className="form-field">
            <label>节奏控制</label>
            <select disabled>
              <option value="medium">中等（默认）</option>
              <option value="fast">快节奏</option>
              <option value="slow">慢节奏</option>
            </select>
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
        </div>
      </CollapsePanel>
    </div>
  );
}
