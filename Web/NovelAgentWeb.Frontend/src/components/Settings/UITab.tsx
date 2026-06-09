import CollapsePanel from './CollapsePanel';
import type { UserSettings } from '../../api/types';

interface UITabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
}

export default function UITab({ form, update }: UITabProps) {
  return (
    <div className="settings-tab-content">
      {/* 4.1 Theme Settings */}
      <CollapsePanel id="ui-1" title="主题设置" defaultOpen={true}>
        <div className="form-grid">
          <div className="form-field">
            <label>主题</label>
            <select value={form.theme || 'dark'} onChange={(e) => update('theme', e.target.value)}>
              <option value="dark">深色（默认）</option>
              <option value="light">浅色</option>
            </select>
          </div>
          <div className="form-field">
            <label>自定义主题色</label>
            <p className="settings-desc" style={{ marginBottom: 0 }}>功能即将推出</p>
          </div>
        </div>
      </CollapsePanel>

      {/* 4.2 Language Settings */}
      <CollapsePanel id="ui-2" title="语言设置" defaultOpen={false}>
        <div className="form-grid">
          <div className="form-field">
            <label>界面语言</label>
            <select value={form.language || 'zh-CN'} onChange={(e) => update('language', e.target.value)}>
              <option value="zh-CN">简体中文</option>
              <option value="en">English</option>
            </select>
          </div>
        </div>
      </CollapsePanel>

      {/* 4.3 Editor Config */}
      <CollapsePanel id="ui-3" title="编辑器配置" defaultOpen={false}>
        <div className="form-grid">
          <div className="form-field">
            <label>
              <input
                type="checkbox"
                checked={form.showStepDetails ?? true}
                onChange={(e) => update('showStepDetails', e.target.checked)}
              />
              显示 Agent 执行步骤详情
            </label>
          </div>
          <div className="form-field">
            <label>字体大小</label>
            <select disabled>
              <option value="medium">中等（默认）</option>
              <option value="small">小</option>
              <option value="large">大</option>
            </select>
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
          <div className="form-field">
            <label>行间距</label>
            <select disabled>
              <option value="normal">正常（默认）</option>
              <option value="compact">紧凑</option>
              <option value="relaxed">宽松</option>
            </select>
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
          <div className="form-field">
            <label>键盘快捷键</label>
            <button className="ghost-button" disabled>配置快捷键</button>
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
        </div>
      </CollapsePanel>
    </div>
  );
}
