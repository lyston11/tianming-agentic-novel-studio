import { useState } from 'react';
import CollapsePanel from './CollapsePanel';
import type { UserSettings, LlmPreset } from '../../api/types';

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
}: AIConfigTabProps) {
  const [showApiKey, setShowApiKey] = useState(false);
  const [showEmbeddingApiKey, setShowEmbeddingApiKey] = useState(false);

  return (
    <div className="settings-tab-content">
      {/* 2.1 LLM Config */}
      <CollapsePanel id="ai-1" title="大模型配置" defaultOpen={true}>
        <div className="form-grid">
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
            <select value={form.llmProvider || 'openai'} onChange={(e) => update('llmProvider', e.target.value)}>
              {Object.entries(PROVIDER_LABELS).map(([k, v]) => (
                <option key={k} value={k}>{v}</option>
              ))}
            </select>
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
            <select value={form.embeddingProvider || 'local'} onChange={(e) => update('embeddingProvider', e.target.value)}>
              <option value="local">本地 ONNX 模型</option>
              <option value="openai">OpenAI Embedding</option>
              <option value="custom">自定义</option>
            </select>
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
