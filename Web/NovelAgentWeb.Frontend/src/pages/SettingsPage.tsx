import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getSettings, resetSettings, saveSettings, testConnection } from '../api';
import type { UserSettings, LlmPreset } from '../api/types';
import Topbar from '../components/layout/Topbar';
import AccountTab from '../components/Settings/AccountTab';
import AIConfigTab from '../components/Settings/AIConfigTab';
import CreativeTab from '../components/Settings/CreativeTab';
import UITab from '../components/Settings/UITab';
import { useSettingsUIStore } from '../stores/settingsUIStore';
import '../styles/settings.css';
import '../styles/collapse-panel.css';

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
  const { activeTab, setActiveTab } = useSettingsUIStore();
  const [form, setForm] = useState<Partial<UserSettings>>({});
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [saveStatus, setSaveStatus] = useState<{ success: boolean; message: string } | null>(null);
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
    { key: 'account' as const, label: '账号管理' },
    { key: 'ai' as const, label: 'AI 配置' },
    { key: 'creative' as const, label: '创作设置' },
    { key: 'ui' as const, label: '界面偏好' },
  ];

  const hasCustomConfig = hasCustomLlmConfig(form, settings?.presets ?? []);

  const canTestConnection = (() => {
    if (!form.llmBaseUrl || !form.llmModel) return false;
    const needsApiKey = form.llmProvider && form.llmProvider !== 'ollama';
    if (needsApiKey && !form.llmApiKey) return false;
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
          {activeTab === 'account' && (
            <AccountTab form={form} update={update} />
          )}

          {activeTab === 'ai' && (
            <AIConfigTab
              form={form}
              update={update}
              settings={settings}
              testMutation={testMutation}
              testResult={testResult}
              validationErrors={validationErrors}
              canTestConnection={canTestConnection}
              applyPreset={applyPreset}
              hasCustomConfig={hasCustomConfig}
              samePreset={samePreset}
            />
          )}

          {activeTab === 'creative' && (
            <CreativeTab form={form} update={update} />
          )}

          {activeTab === 'ui' && (
            <UITab form={form} update={update} />
          )}
        </div>
      </div>
    </>
  );
}
