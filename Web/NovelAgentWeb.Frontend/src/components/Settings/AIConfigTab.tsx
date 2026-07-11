import { useState } from 'react';
import CollapsePanel from './CollapsePanel';
import SettingsSelect from './SettingsSelect';
import type { UserSettings, LlmPreset, LlmConnectionHealth } from '../../api/types';

interface AIConfigTabProps {
  form: Partial<UserSettings>;
  update: (key: keyof UserSettings, value: unknown) => void;
  settings: UserSettings | undefined;
  testMutation: { mutate: () => void; isPending: boolean };
  testResult: { success: boolean; message: string } | null;
  validationErrors: string[];
  canTestConnection: boolean;
  applyPreset: (preset: LlmPreset) => void;
  hasCustomConfig: boolean;
  samePreset: (preset: LlmPreset, form: Partial<UserSettings>) => boolean;
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

export default function AIConfigTab({
  form,
  update,
  settings,
  testMutation,
  testResult,
  validationErrors,
  canTestConnection,
  applyPreset,
  hasCustomConfig,
  samePreset,
  llmHealth,
  isLlmHealthFetching,
  onRefreshLlmHealth,
  hasUnsavedChanges,
}: AIConfigTabProps) {
  const [showApiKey, setShowApiKey] = useState(false);
  const [showEmbeddingApiKey, setShowEmbeddingApiKey] = useState(false);

  return (
    <div className="settings-tab-content">
      {/* 2.1 LLM Config */}
      <CollapsePanel id="ai-1" title="大模型配置" defaultOpen={true}>
        <div className="form-grid">
          <div className={`llm-health-card ${llmHealth?.status ?? 'unknown'}`}>
            <div className="llm-health-main">
              <span className="llm-health-dot" />
              <div className="llm-health-copy">
                <strong>{healthTitle(llmHealth, isLlmHealthFetching)}</strong>
                <span>{healthMessage(llmHealth, isLlmHealthFetching, hasUnsavedChanges)}</span>
              </div>
            </div>
            <button
              className="ghost-button compact"
              type="button"
              onClick={onRefreshLlmHealth}
              disabled={isLlmHealthFetching}
            >
              {isLlmHealthFetching ? '检测中...' : '重新检测'}
            </button>
          </div>

          {/* Presets */}
          <div className="preset-grid">
            {hasCustomConfig && (
              <button className="preset-btn active custom-current" type="button">
                <span className="preset-name">当前配置</span>
                <span className="preset-model">{form.llmModel || '自定义模型'}</span>
              </button>
            )}
            {(settings?.presets ?? []).map((preset) => (
              <button
                key={preset.name}
                className={`preset-btn${samePreset(preset, form) ? ' active' : ''}`}
                onClick={() => applyPreset(preset)}
                type="button"
              >
                <span className="preset-name">{preset.name}</span>
                <span className="preset-model">{preset.model || '自定义'}</span>
              </button>
            ))}
          </div>

          <div className="form-field">
            <label>服务商</label>
            <SettingsSelect
              value={form.llmProvider || 'openai'}
              options={PROVIDER_OPTIONS}
              onChange={(value) => update('llmProvider', value)}
            />
          </div>

          <div className="form-field">
            <label>API Base URL</label>
            <input
              value={form.llmBaseUrl || ''}
              onChange={(e) => update('llmBaseUrl', e.target.value)}
              placeholder="https://api.openai.com/v1"
            />
          </div>

          <div className="form-field">
            <label>
              API Key
              <button className="toggle-key" type="button" onClick={() => setShowApiKey(!showApiKey)}>
                {showApiKey ? '隐藏' : '显示'}
              </button>
            </label>
            <input
              type={showApiKey ? 'text' : 'password'}
              value={form.llmApiKey || ''}
              onChange={(e) => update('llmApiKey', e.target.value)}
              placeholder="sk-..."
            />
          </div>

          <div className="form-field">
            <label>模型</label>
            <input
              value={form.llmModel || ''}
              onChange={(e) => update('llmModel', e.target.value)}
              placeholder="gpt-4o"
            />
          </div>

          <div className="form-row">
            <div className="form-field">
              <label>Temperature ({(form.llmTemperature ?? 0.7).toFixed(1)})</label>
              <div className="slider-with-marks">
                <input
                  type="range"
                  min="0"
                  max="2"
                  step="0.1"
                  value={form.llmTemperature ?? 0.7}
                  onChange={(e) => update('llmTemperature', parseFloat(e.target.value))}
                  list="temperature-marks"
                />
                <datalist id="temperature-marks">
                  <option value="0" label="0"></option>
                  <option value="0.7" label="0.7"></option>
                  <option value="1.0" label="1.0"></option>
                  <option value="2.0" label="2.0"></option>
                </datalist>
                <div className="slider-labels">
                  <span>确定性</span>
                  <span>平衡</span>
                  <span>创造性</span>
                </div>
              </div>
            </div>
            <div className="form-field">
              <label>Max Tokens</label>
              <input
                type="number"
                value={form.llmMaxTokens ?? 4096}
                min={256}
                max={200000}
                onChange={(e) => update('llmMaxTokens', Number(e.target.value))}
              />
            </div>
          </div>

          {/* Test Connection */}
          <div className="test-section">
            <button
              className="ghost-button"
              onClick={() => testMutation.mutate()}
              disabled={testMutation.isPending || !canTestConnection}
              title={
                !canTestConnection
                  ? !form.llmBaseUrl
                    ? '请先填写 API Base URL'
                    : !form.llmModel
                    ? '请先填写模型名称'
                    : '请先填写 API Key'
                  : ''
              }
            >
              {testMutation.isPending ? '测试中...' : '测试连接'}
            </button>
            {testResult && (
              <span className={`test-result ${testResult.success ? 'success' : 'error'}`}>
                {testResult.message}
              </span>
            )}
          </div>

          {validationErrors.length > 0 && (
            <div className="validation-errors">
              {validationErrors.map((error, idx) => (
                <div key={idx} className="validation-error">⚠ {error}</div>
              ))}
            </div>
          )}
        </div>
      </CollapsePanel>

      {/* 2.2 Embedding Config */}
      <CollapsePanel id="ai-2" title="Embedding 配置" defaultOpen={false}>
        <div className="form-grid">
          <p className="settings-desc">配置用于 RAG 检索的 Embedding 模型。本地模型无需联网即可使用。</p>
          <div className="form-field">
            <label>Provider</label>
            <SettingsSelect
              value={form.embeddingProvider || 'local'}
              options={EMBEDDING_PROVIDER_OPTIONS}
              onChange={(value) => update('embeddingProvider', value)}
            />
          </div>
          <div className="form-field">
            <label>模型</label>
            <input value={form.embeddingModel || ''} onChange={(e) => update('embeddingModel', e.target.value)} />
          </div>
          {form.embeddingProvider !== 'local' && (
            <>
              <div className="form-field">
                <label>Base URL</label>
                <input value={form.embeddingBaseUrl || ''} onChange={(e) => update('embeddingBaseUrl', e.target.value)} />
              </div>
              <div className="form-field">
                <label>
                  API Key
                  <button className="toggle-key" type="button" onClick={() => setShowEmbeddingApiKey(!showEmbeddingApiKey)}>
                    {showEmbeddingApiKey ? '隐藏' : '显示'}
                  </button>
                </label>
                <input
                  type={showEmbeddingApiKey ? 'text' : 'password'}
                  value={form.embeddingApiKey || ''}
                  onChange={(e) => update('embeddingApiKey', e.target.value)}
                />
              </div>
            </>
          )}
        </div>
      </CollapsePanel>

      {/* 2.3 Presets Management */}
      <CollapsePanel id="ai-3" title="预设管理" defaultOpen={false}>
        <div className="form-grid">
          <p className="settings-desc">保存常用的配置预设，方便快速切换。</p>
          <div className="form-field">
            <label>功能即将推出</label>
            <button className="ghost-button" disabled>保存当前配置为预设</button>
          </div>
        </div>
      </CollapsePanel>
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
