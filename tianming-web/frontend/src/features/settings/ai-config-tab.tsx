import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Slider } from '@/components/ui/slider';
import { Spinner } from '@/components/ui/spinner';
import type { LlmConnectionHealth, LlmPreset, UserSettings } from '@/api/types';
import { SettingsSection, SettingsField, SettingsHint } from './settings-section';
import { SettingsSelect } from './settings-select';

export interface AiConfigTabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
  presets: LlmPreset[];
  samePreset: (preset: LlmPreset, form: Partial<UserSettings>) => boolean;
  applyPreset: (preset: LlmPreset) => void;
  hasCustomConfig: boolean;
  canTestConnection: boolean;
  onTestConnection: () => void;
  isTestPending: boolean;
  testResult: { success: boolean; message: string } | null;
  validationErrors: string[];
  llmHealth?: LlmConnectionHealth;
  isLlmHealthFetching: boolean;
  onRefreshLlmHealth: () => void;
  hasUnsavedChanges: boolean;
}

const PROVIDER_LABELS: Record<string, string> = {
  openai: 'OpenAI',
  anthropic: 'Anthropic',
  deepseek: 'DeepSeek',
  moonshot: 'Moonshot (Kimi)',
  zhipu: 'Zhipu (GLM)',
  qwen: 'Qwen (通义千问)',
  ollama: 'Local (Ollama)',
  custom: '自定义',
};

const PROVIDER_OPTIONS = Object.entries(PROVIDER_LABELS).map(([value, label]) => ({ value, label }));

const EMBEDDING_PROVIDER_OPTIONS = [
  { value: 'local', label: '本地 ONNX 模型' },
  { value: 'openai', label: 'OpenAI Embedding' },
  { value: 'custom', label: '自定义' },
];

export function AiConfigTab({
  form,
  update,
  presets,
  samePreset,
  applyPreset,
  hasCustomConfig,
  canTestConnection,
  onTestConnection,
  isTestPending,
  testResult,
  validationErrors,
  llmHealth,
  isLlmHealthFetching,
  onRefreshLlmHealth,
  hasUnsavedChanges,
}: AiConfigTabProps) {
  const [showApiKey, setShowApiKey] = useState(false);
  const [showEmbeddingApiKey, setShowEmbeddingApiKey] = useState(false);

  return (
    <div className="space-y-4">
      <SettingsSection title="大模型配置" defaultOpen>
        <div className="flex items-center justify-between rounded-lg border bg-muted/40 px-3 py-2.5">
          <div className="flex items-center gap-2.5 text-sm">
            <span
              className={`inline-block size-2 rounded-full ${
                llmHealth?.status === 'ready'
                  ? 'bg-emerald-500'
                  : llmHealth?.status && llmHealth.status !== 'unknown'
                    ? 'bg-destructive'
                    : 'bg-muted-foreground/40'
              }`}
            />
            <div>
              <div className="font-medium">{healthTitle(llmHealth, isLlmHealthFetching)}</div>
              <div className="text-xs text-muted-foreground">
                {healthMessage(llmHealth, isLlmHealthFetching, hasUnsavedChanges)}
              </div>
            </div>
          </div>
          <Button variant="ghost" size="sm" onClick={onRefreshLlmHealth} disabled={isLlmHealthFetching}>
            {isLlmHealthFetching && <Spinner />}
            {isLlmHealthFetching ? '检测中...' : '重新检测'}
          </Button>
        </div>

        <SettingsField label="预设">
          <div className="flex flex-wrap gap-2">
            {hasCustomConfig && (
              <span className="rounded-lg border border-primary/40 bg-primary/10 px-3 py-1.5 text-xs">
                当前配置 · {form.llmModel || '自定义模型'}
              </span>
            )}
            {presets.map((preset) => (
              <button
                key={preset.name}
                type="button"
                onClick={() => applyPreset(preset)}
                className={`rounded-lg border px-3 py-1.5 text-left text-xs transition-colors ${
                  samePreset(preset, form)
                    ? 'border-primary/40 bg-primary/10'
                    : 'bg-background hover:bg-muted'
                }`}
              >
                <span className="block font-medium">{preset.name}</span>
                <span className="text-muted-foreground">{preset.model || '自定义'}</span>
              </button>
            ))}
          </div>
        </SettingsField>

        <div className="grid gap-4 sm:grid-cols-2">
          <SettingsField label="服务商">
            <SettingsSelect
              value={form.llmProvider || 'openai'}
              options={PROVIDER_OPTIONS}
              onChange={(value) => update('llmProvider', value)}
            />
          </SettingsField>
          <SettingsField label="模型">
            <Input
              value={form.llmModel || ''}
              onChange={(event) => update('llmModel', event.target.value)}
              placeholder="gpt-4o"
            />
          </SettingsField>
        </div>

        <SettingsField label="API Base URL">
          <Input
            value={form.llmBaseUrl || ''}
            onChange={(event) => update('llmBaseUrl', event.target.value)}
            placeholder="https://api.openai.com/v1"
          />
        </SettingsField>

        <SettingsField label="API Key">
          <div className="flex gap-2">
            <Input
              type={showApiKey ? 'text' : 'password'}
              value={form.llmApiKey || ''}
              onChange={(event) => update('llmApiKey', event.target.value)}
              placeholder="sk-..."
            />
            <Button variant="outline" size="sm" type="button" onClick={() => setShowApiKey(!showApiKey)}>
              {showApiKey ? '隐藏' : '显示'}
            </Button>
          </div>
        </SettingsField>

        <div className="grid gap-4 sm:grid-cols-2">
          <SettingsField label={`Temperature (${(form.llmTemperature ?? 0.7).toFixed(1)})`}>
            <div className="space-y-1.5">
              <Slider
                min={0}
                max={2}
                step={0.1}
                value={[form.llmTemperature ?? 0.7]}
                onValueChange={([value]) => update('llmTemperature', value)}
              />
              <div className="flex justify-between text-[11px] text-muted-foreground">
                <span>确定性</span>
                <span>平衡</span>
                <span>创造性</span>
              </div>
            </div>
          </SettingsField>
          <SettingsField label="Max Tokens">
            <Input
              type="number"
              value={form.llmMaxTokens ?? 4096}
              min={256}
              max={200000}
              onChange={(event) => update('llmMaxTokens', Number(event.target.value))}
            />
          </SettingsField>
        </div>

        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            onClick={onTestConnection}
            disabled={isTestPending || !canTestConnection}
            title={
              !canTestConnection
                ? !form.llmBaseUrl
                  ? '请先填写 API Base URL'
                  : !form.llmModel
                    ? '请先填写模型名称'
                    : '请先填写 API Key'
                : undefined
            }
          >
            {isTestPending ? '测试中...' : '测试连接'}
          </Button>
          {testResult && (
            <span className={`text-sm ${testResult.success ? 'text-emerald-600' : 'text-destructive'}`}>
              {testResult.message}
            </span>
          )}
        </div>

        {validationErrors.length > 0 && (
          <div className="space-y-1 rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2">
            {validationErrors.map((error, index) => (
              <div key={index} className="text-xs text-destructive">⚠ {error}</div>
            ))}
          </div>
        )}
      </SettingsSection>

      <SettingsSection title="Embedding 配置">
        <SettingsHint>配置用于 RAG 检索的 Embedding 模型。本地模型无需联网即可使用。</SettingsHint>
        <div className="grid gap-4 sm:grid-cols-2">
          <SettingsField label="Provider">
            <SettingsSelect
              value={form.embeddingProvider || 'local'}
              options={EMBEDDING_PROVIDER_OPTIONS}
              onChange={(value) => update('embeddingProvider', value)}
            />
          </SettingsField>
          <SettingsField label="模型">
            <Input value={form.embeddingModel || ''} onChange={(event) => update('embeddingModel', event.target.value)} />
          </SettingsField>
        </div>
        {form.embeddingProvider !== 'local' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <SettingsField label="Base URL">
              <Input value={form.embeddingBaseUrl || ''} onChange={(event) => update('embeddingBaseUrl', event.target.value)} />
            </SettingsField>
            <SettingsField label="API Key">
              <div className="flex gap-2">
                <Input
                  type={showEmbeddingApiKey ? 'text' : 'password'}
                  value={form.embeddingApiKey || ''}
                  onChange={(event) => update('embeddingApiKey', event.target.value)}
                />
                <Button
                  variant="outline"
                  size="sm"
                  type="button"
                  onClick={() => setShowEmbeddingApiKey(!showEmbeddingApiKey)}
                >
                  {showEmbeddingApiKey ? '隐藏' : '显示'}
                </Button>
              </div>
            </SettingsField>
          </div>
        )}
      </SettingsSection>

      <SettingsSection title="预设管理">
        <SettingsHint>保存常用的配置预设，方便快速切换。功能即将推出。</SettingsHint>
        <Button variant="outline" size="sm" disabled>保存当前配置为预设</Button>
      </SettingsSection>
    </div>
  );
}

function healthTitle(health: LlmConnectionHealth | undefined, loading: boolean): string {
  if (loading && !health) return '正在检测模型连接';
  switch (health?.status) {
    case 'ready':
      return '模型连接可用';
    case 'authentication_failed':
      return '模型认证失败';
    case 'api_key_unavailable':
      return 'API Key 不可用';
    case 'missing_config':
      return '模型配置不完整';
    case 'connection_failed':
      return '模型连接失败';
    case 'provider_error':
      return '模型服务异常';
    default:
      return '模型状态未知';
  }
}

function healthMessage(
  health: LlmConnectionHealth | undefined,
  loading: boolean,
  hasUnsavedChanges: boolean,
): string {
  if (loading && !health) return '正在读取当前已保存配置。';
  if (!health) return '还没有当前配置的健康检查结果。';
  const savedNotice = hasUnsavedChanges ? '（当前表单有未保存改动）' : '';
  const detail = health.recommendedAction || health.message || '请检查模型配置。';
  const target = [health.provider, health.model].filter(Boolean).join(' / ');
  return `${target || '未配置模型'}：${detail}${savedNotice}`;
}
