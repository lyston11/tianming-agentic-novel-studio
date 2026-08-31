import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import type { UserSettings } from '@/api/types';
import { SettingsSection, SettingsField, SettingsHint } from './settings-section';
import { SettingsSelect } from './settings-select';

interface CreativeTabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
}

const RISK_OPTIONS = [
  { value: 'Low', label: '低 - 自动执行所有步骤' },
  { value: 'Medium', label: '中 - 高风险步骤需确认' },
  { value: 'High', label: '高 - 所有步骤需确认' },
];

const WRITING_STYLE_OPTIONS = [
  { value: 'balanced', label: '平衡（默认）' },
  { value: 'concise', label: '简洁' },
  { value: 'detailed', label: '详细' },
];

const PACING_OPTIONS = [
  { value: 'medium', label: '中等（默认）' },
  { value: 'fast', label: '快节奏' },
  { value: 'slow', label: '慢节奏' },
];

export function CreativeTab({ form, update }: CreativeTabProps) {
  return (
    <div className="space-y-4">
      <SettingsSection title="Agent 行为" defaultOpen>
        <SettingsHint>控制 Agent loop 的风险阈值和推进步数。</SettingsHint>
        <SettingsField label="默认风险等级">
          <SettingsSelect
            value={form.agentDefaultRisk || 'Medium'}
            options={RISK_OPTIONS}
            onChange={(value) => update('agentDefaultRisk', value)}
          />
        </SettingsField>
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium text-foreground/90">Agent loop 自动推进</span>
          <Switch
            checked={form.agentLoopAutoProceed ?? true}
            onCheckedChange={(checked) => update('agentLoopAutoProceed', checked)}
          />
        </div>
        <SettingsField label="最大循环步数">
          <Input
            type="number"
            value={form.agentLoopMaxSteps ?? 12}
            min={1}
            max={100}
            onChange={(event) => update('agentLoopMaxSteps', Number(event.target.value))}
          />
        </SettingsField>
      </SettingsSection>

      <SettingsSection title="创作默认值">
        <SettingsHint>新建创作时的默认参数。</SettingsHint>
        <div className="grid gap-4 sm:grid-cols-2">
          <SettingsField label="默认类型">
            <Input
              value={form.defaultGenre || ''}
              onChange={(event) => update('defaultGenre', event.target.value)}
              placeholder="玄幻"
            />
          </SettingsField>
          <SettingsField label="默认子类型">
            <Input value={form.defaultSubGenre || ''} onChange={(event) => update('defaultSubGenre', event.target.value)} />
          </SettingsField>
          <SettingsField label="默认章节字数">
            <Input
              type="number"
              min={500}
              max={50000}
              value={form.defaultChapterWordCount ?? 3000}
              onChange={(event) => update('defaultChapterWordCount', Number(event.target.value))}
            />
          </SettingsField>
          <SettingsField label="默认每卷章节数">
            <Input
              type="number"
              min={1}
              max={200}
              value={form.defaultVolumeChapterCount ?? 6}
              onChange={(event) => update('defaultVolumeChapterCount', Number(event.target.value))}
            />
          </SettingsField>
        </div>
      </SettingsSection>

      <SettingsSection title="写作偏好">
        <SettingsField label="写作风格">
          <SettingsSelect value="balanced" options={WRITING_STYLE_OPTIONS} disabled />
          <SettingsHint>功能即将推出</SettingsHint>
        </SettingsField>
        <SettingsField label="节奏控制">
          <SettingsSelect value="medium" options={PACING_OPTIONS} disabled />
          <SettingsHint>功能即将推出</SettingsHint>
        </SettingsField>
      </SettingsSection>
    </div>
  );
}
