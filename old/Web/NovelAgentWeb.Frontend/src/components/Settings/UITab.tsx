import CollapsePanel from './CollapsePanel';
import SettingsSelect from './SettingsSelect';
import type { UserSettings } from '../../api/types';

interface UITabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
}

export default function UITab({ form, update }: UITabProps) {
  const themeOptions = [
    { value: 'dark', label: '深色（默认）' },
    { value: 'light', label: '浅色' },
  ];
  const languageOptions = [
    { value: 'zh-CN', label: '简体中文' },
    { value: 'en', label: 'English' },
  ];
  const fontSizeOptions = [
    { value: 'medium', label: '中等（默认）' },
    { value: 'small', label: '小' },
    { value: 'large', label: '大' },
  ];
  const lineHeightOptions = [
    { value: 'normal', label: '正常（默认）' },
    { value: 'compact', label: '紧凑' },
    { value: 'relaxed', label: '宽松' },
  ];

  return (
    <div className="settings-tab-content">
      {/* 4.1 Theme Settings */}
      <CollapsePanel id="ui-1" title="主题设置" defaultOpen={true}>
        <div className="form-grid">
          <div className="form-field">
            <label>主题</label>
            <SettingsSelect
              value={form.theme || 'dark'}
              options={themeOptions}
              onChange={(value) => update('theme', value)}
            />
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
            <SettingsSelect
              value={form.language || 'zh-CN'}
              options={languageOptions}
              onChange={(value) => update('language', value)}
            />
          </div>
        </div>
      </CollapsePanel>

      {/* 4.3 Editor Config */}
      <CollapsePanel id="ui-3" title="编辑器配置" defaultOpen={false}>
        <div className="form-grid">
          <div className="form-field">
            <label>字体大小</label>
            <SettingsSelect value="medium" options={fontSizeOptions} disabled />
            <p className="settings-desc" style={{ marginTop: 4, marginBottom: 0 }}>功能即将推出</p>
          </div>
          <div className="form-field">
            <label>行间距</label>
            <SettingsSelect value="normal" options={lineHeightOptions} disabled />
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
