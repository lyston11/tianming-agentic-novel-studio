import { Button } from '@/components/ui/button';
import type { UserSettings } from '@/api/types';
import { SettingsSection, SettingsField, SettingsHint } from './settings-section';
import { SettingsSelect } from './settings-select';

interface UiTabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
}

const THEME_OPTIONS = [
  { value: 'light', label: '浅色（纸墨）' },
  { value: 'dark', label: '深色' },
];

const LANGUAGE_OPTIONS = [
  { value: 'zh-CN', label: '简体中文' },
  { value: 'en', label: 'English' },
];

const FONT_SIZE_OPTIONS = [
  { value: 'medium', label: '中等（默认）' },
  { value: 'small', label: '小' },
  { value: 'large', label: '大' },
];

const LINE_HEIGHT_OPTIONS = [
  { value: 'normal', label: '正常（默认）' },
  { value: 'compact', label: '紧凑' },
  { value: 'relaxed', label: '宽松' },
];

export function UiTab({ form, update }: UiTabProps) {
  return (
    <div className="space-y-4">
      <SettingsSection title="主题设置" defaultOpen>
        <SettingsField label="主题">
          <SettingsSelect
            value={form.theme || 'light'}
            options={THEME_OPTIONS}
            onChange={(value) => update('theme', value)}
          />
        </SettingsField>
        <SettingsHint>自定义主题色功能即将推出。</SettingsHint>
      </SettingsSection>

      <SettingsSection title="语言设置">
        <SettingsField label="界面语言">
          <SettingsSelect
            value={form.language || 'zh-CN'}
            options={LANGUAGE_OPTIONS}
            onChange={(value) => update('language', value)}
          />
        </SettingsField>
      </SettingsSection>

      <SettingsSection title="编辑器配置">
        <SettingsField label="字体大小">
          <SettingsSelect value="medium" options={FONT_SIZE_OPTIONS} disabled />
          <SettingsHint>功能即将推出</SettingsHint>
        </SettingsField>
        <SettingsField label="行间距">
          <SettingsSelect value="normal" options={LINE_HEIGHT_OPTIONS} disabled />
          <SettingsHint>功能即将推出</SettingsHint>
        </SettingsField>
        <SettingsField label="键盘快捷键">
          <Button variant="outline" size="sm" disabled>配置快捷键</Button>
          <SettingsHint>功能即将推出</SettingsHint>
        </SettingsField>
      </SettingsSection>
    </div>
  );
}
