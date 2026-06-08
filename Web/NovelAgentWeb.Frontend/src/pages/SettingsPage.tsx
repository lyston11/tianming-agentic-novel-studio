import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getSettings, resetSettings, saveSettings, testConnection } from '../api';
import type { UserSettings, LlmPreset } from '../api/types';
import Topbar from '../components/layout/Topbar';
import '../styles/settings.css';

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

function samePreset(preset: LlmPreset, form: Partial<UserSettings>) {
  // Only match provider/baseUrl/model - API key changes don't invalidate preset match
  return (
    form.llmProvider === preset.provider &&
    (form.llmBaseUrl || '').trim() === preset.baseUrl &&
    (form.llmModel || '').trim() === preset.model
  );
}

function hasCustomLlmConfig(
  form: Partial<UserSettings>,
  presets: LlmPreset[]
): boolean {
  if (!form.llmProvider || (!form.llmBaseUrl && !form.llmModel)) return false;
  return !presets.some((preset) => samePreset(preset, form));
}

function isValidUrl(urlString: string): boolean {
  if (!urlString || !urlString.trim()) return false;
  try {
    const url = new URL(urlString.trim());
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

function validateLlmConfig(form: Partial<UserSettings>): string[] {
  const errors: string[] = [];

  if (!form.llmProvider) errors.push('请选择服务商');
  if (!form.llmBaseUrl || !form.llmBaseUrl.trim()) errors.push('请填写 API Base URL');
  else if (!isValidUrl(form.llmBaseUrl)) errors.push('API Base URL 格式无效（需要 http:// 或 https://）');

  if (!form.llmModel || !form.llmModel.trim()) errors.push('请填写模型名称');

  const needsApiKey = form.llmProvider && form.llmProvider !== 'ollama';
  if (needsApiKey && (!form.llmApiKey || !form.llmApiKey.trim() || form.llmApiKey.includes('****'))) {
    errors.push('请填写 API Key');
  }

  const temp = form.llmTemperature ?? 0.7;
  if (temp < 0 || temp > 2) errors.push('Temperature 必须在 0-2 之间');

  const maxTokens = form.llmMaxTokens ?? 4096;
  if (maxTokens < 256 || maxTokens > 200000) errors.push('Max Tokens 必须在 256-200000 之间');

  return errors;
}

export default function SettingsPage() {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<Partial<UserSettings>>({});
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [saveStatus, setSaveStatus] = useState<{ success: boolean; message: string } | null>(null);
  const [showApiKey, setShowApiKey] = useState(false);
  const [showEmbeddingApiKey, setShowEmbeddingApiKey] = useState(false);
  const [activeTab, setActiveTab] = useState<'llm' | 'embedding' | 'agent' | 'generation' | 'ui'>('llm');
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);

  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: getSettings });

  useEffect(() => {
    if (settings) setForm(settings);
  }, [settings]);

  // Detect unsaved changes
  useEffect(() => {
    if (!settings) return;
    const changed = JSON.stringify(form) !== JSON.stringify(settings);
    setHasUnsavedChanges(changed);
  }, [form, settings]);

  // Warn before leaving with unsaved changes
  useEffect(() => {
    const handleBeforeUnload = (e: BeforeUnloadEvent) => {
      if (hasUnsavedChanges) {
        e.preventDefault();
        e.returnValue = '';
      }
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [hasUnsavedChanges]);

  const saveMutation = useMutation({
    mutationFn: () => {
      const errors = validateLlmConfig(form);
      if (errors.length > 0) {
        throw new Error(errors.join('；'));
      }
      return saveSettings(form);
    },
    onSuccess: (res) => {
      queryClient.invalidateQueries({ queryKey: ['settings'] });
      setSaveStatus({ success: res.success, message: res.message });
      setHasUnsavedChanges(false);
    },
    onError: (err) => setSaveStatus({ success: false, message: `${err}` }),
  });

  const testMutation = useMutation({
    mutationFn: () => testConnection(form),
    onSuccess: (res) => setTestResult(res),
    onError: (err) => setTestResult({ success: false, message: `${err}` }),
  });

  const resetMutation = useMutation({
    mutationFn: resetSettings,
    onSuccess: (res) => {
      setForm(res);
      setTestResult(null);
      setSaveStatus({ success: true, message: '已恢复默认设置' });
      queryClient.setQueryData(['settings'], res);
    },
    onError: (err) => setSaveStatus({ success: false, message: `恢复失败: ${err}` }),
  });

  const update = (key: keyof UserSettings, value: unknown) => {
    // Only clear save status if it was an error - keep success messages visible
    if (saveStatus && !saveStatus.success) setSaveStatus(null);
    // Clear test result when any LLM config changes (including API key)
    if (key.toString().startsWith('llm')) setTestResult(null);

    const newForm = { ...form, [key]: value };
    setForm(newForm);

    // Run validation after update to provide immediate feedback
    if (key.toString().startsWith('llm')) {
      setValidationErrors(validateLlmConfig(newForm));
    }
  };

  const applyPreset = (preset: LlmPreset) => {
    const newForm = {
      ...form,
      llmProvider: preset.provider,
      llmBaseUrl: preset.baseUrl,
      llmModel: preset.model,
    };
    setForm(newForm);
    setSaveStatus(null);
    setTestResult(null);
    setValidationErrors(validateLlmConfig(newForm));
  };

  const tabs = [
    { key: 'llm' as const, label: '大模型配置' },
    { key: 'embedding' as const, label: 'Embedding 模型' },
    { key: 'agent' as const, label: 'Agent 设置' },
    { key: 'generation' as const, label: '创作默认值' },
    { key: 'ui' as const, label: '界面偏好' },
  ];

  const matchedPreset = (settings?.presets ?? []).find((preset) => samePreset(preset, form));
  const hasCustomConfig = hasCustomLlmConfig(form, settings?.presets ?? []);

  const canTestConnection = (() => {
    if (!form.llmBaseUrl || !form.llmModel) return false;
    const needsApiKey = form.llmProvider && form.llmProvider !== 'ollama';
    if (needsApiKey && (!form.llmApiKey || form.llmApiKey.includes('****'))) return false;
    return true;
  })();

  return (
    <>
      <Topbar
        title="用户设置"
        actions={
          <div className="settings-top-actions">
            {hasUnsavedChanges && !saveStatus && (
              <span className="unsaved-indicator">● 未保存的更改</span>
            )}
            {saveStatus && (
              <span className={`settings-save-status ${saveStatus.success ? 'success' : 'error'}`}>
                {saveStatus.message}
              </span>
            )}
            <button
              className="ghost-button"
              onClick={() => resetMutation.mutate()}
              disabled={resetMutation.isPending || saveMutation.isPending}
            >
              {resetMutation.isPending ? '恢复中...' : '恢复默认'}
            </button>
            <button
              className="ink-button"
              onClick={() => saveMutation.mutate()}
              disabled={saveMutation.isPending || resetMutation.isPending}
            >
              {saveMutation.isPending ? '保存中...' : '保存设置'}
            </button>
          </div>
        }
      />

      <div className="settings-layout">
        <div className="settings-tabs">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              className={`settings-tab${activeTab === tab.key ? ' active' : ''}`}
              onClick={() => setActiveTab(tab.key)}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div className="settings-content">
          {/* LLM Configuration */}
          {activeTab === 'llm' && (
            <div className="settings-section">
              <h3>大模型配置</h3>
              <p className="settings-desc">配置 AI 大语言模型的连接信息。支持 OpenAI 兼容接口。</p>

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

              <div className="form-grid">
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
              </div>

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
          )}

          {/* Embedding Configuration */}
          {activeTab === 'embedding' && (
            <div className="settings-section">
              <h3>Embedding 模型</h3>
              <p className="settings-desc">配置用于 RAG 检索的 Embedding 模型。本地模型无需联网即可使用。</p>
              <div className="form-grid">
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
            </div>
          )}

          {/* Agent Settings */}
          {activeTab === 'agent' && (
            <div className="settings-section">
              <h3>Agent 行为</h3>
              <p className="settings-desc">控制 Agent 自动执行的风险阈值和步数限制。</p>

              <div className="form-grid">
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
            </div>
          )}

          {/* Generation Defaults */}
          {activeTab === 'generation' && (
            <div className="settings-section">
              <h3>创作默认值</h3>
              <p className="settings-desc">新建创作时的默认参数。</p>

              <div className="form-grid">
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
            </div>
          )}

          {/* UI Preferences */}
          {activeTab === 'ui' && (
            <div className="settings-section">
              <h3>界面偏好</h3>
              <div className="form-grid">
                <div className="form-field">
                  <label>主题</label>
                  <select value={form.theme || 'dark'} onChange={(e) => update('theme', e.target.value)}>
                    <option value="dark">深色（默认）</option>
                    <option value="light">浅色</option>
                  </select>
                </div>
                <div className="form-field">
                  <label>语言</label>
                  <select value={form.language || 'zh-CN'} onChange={(e) => update('language', e.target.value)}>
                    <option value="zh-CN">简体中文</option>
                    <option value="en">English</option>
                  </select>
                </div>
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
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}
