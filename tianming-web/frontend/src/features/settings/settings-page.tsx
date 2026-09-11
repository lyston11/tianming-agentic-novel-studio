import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getLlmConnectionHealth,
  getSettings,
  resetSettings,
  saveSettings,
  testConnection,
} from '@/api';
import type { LlmPreset, UserSettings } from '@/api/types';
import { Button } from '@/components/ui/button';
import { PageHeader } from '@/components/shared/page-header';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { AccountTab } from './account-tab';
import { AiConfigTab } from './ai-config-tab';
import { CreativeTab } from './creative-tab';
import { UiTab } from './ui-tab';
import { applyTheme } from '@/lib/theme';

function samePreset(preset: LlmPreset, form: Partial<UserSettings>) {
  // Only match provider/baseUrl/model - API key changes don't invalidate preset match
  return (
    form.llmProvider === preset.provider &&
    (form.llmBaseUrl || '').trim() === preset.baseUrl &&
    (form.llmModel || '').trim() === preset.model
  );
}

function hasCustomLlmConfig(form: Partial<UserSettings>, presets: LlmPreset[]): boolean {
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
  const [activeTab, setActiveTab] = useState('account');
  const [form, setForm] = useState<Partial<UserSettings>>({});
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [saveStatus, setSaveStatus] = useState<{ success: boolean; message: string } | null>(null);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);

  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: getSettings });
  const {
    data: llmHealth,
    isFetching: isLlmHealthFetching,
    refetch: refetchLlmHealth,
  } = useQuery({
    queryKey: ['settings', 'llm-health'],
    queryFn: getLlmConnectionHealth,
    enabled: activeTab === 'ai',
  });

  useEffect(() => {
    if (!settings) return;
    setForm(settings);
  }, [settings]);

  // Live-preview the edited theme; ThemeSync tracks the saved value on load/save.
  // Guard on a loaded value so the fetch window does not clobber the startup theme.
  useEffect(() => {
    if (form.theme) applyTheme(form.theme);
  }, [form.theme]);

  const hasUnsavedChanges = !!settings && JSON.stringify(form) !== JSON.stringify(settings);

  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      if (hasUnsavedChanges) {
        event.preventDefault();
        event.returnValue = '';
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
    onSuccess: (result) => {
      setForm(result);
      queryClient.setQueryData(['settings'], result);
      queryClient.invalidateQueries({ queryKey: ['settings', 'llm-health'] });
      setSaveStatus({ success: true, message: '设置已保存' });
    },
    onError: (error) => setSaveStatus({ success: false, message: `${error}` }),
  });

  const testMutation = useMutation({
    mutationFn: () => testConnection(form),
    onSuccess: (result) => setTestResult(result),
    onError: (error) => setTestResult({ success: false, message: `${error}` }),
  });

  const resetMutation = useMutation({
    mutationFn: resetSettings,
    onSuccess: (result) => {
      setForm(result);
      setTestResult(null);
      setSaveStatus({ success: true, message: '已恢复默认设置' });
      queryClient.setQueryData(['settings'], result);
      queryClient.invalidateQueries({ queryKey: ['settings', 'llm-health'] });
    },
    onError: (error) => setSaveStatus({ success: false, message: `恢复失败: ${error}` }),
  });

  const update = (key: keyof UserSettings, value: unknown) => {
    // Only clear save status if it was an error - keep success messages visible
    if (saveStatus && !saveStatus.success) setSaveStatus(null);
    if (key.toString().startsWith('llm')) setTestResult(null);

    const newForm = { ...form, [key]: value };
    setForm(newForm);

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

  const presets = settings?.presets ?? [];
  const hasCustomConfig = hasCustomLlmConfig(form, presets);

  const canTestConnection = (() => {
    if (!form.llmBaseUrl || !form.llmModel) return false;
    const needsApiKey = form.llmProvider && form.llmProvider !== 'ollama';
    if (needsApiKey && !form.llmApiKey) return false;
    return true;
  })();

  return (
    <div>
      <PageHeader
        title="用户设置"
        actions={
          <>
            {hasUnsavedChanges && !saveStatus && (
              <span className="text-xs text-primary">● 未保存的更改</span>
            )}
            {saveStatus && (
              <span className={`text-xs ${saveStatus.success ? 'text-emerald-600' : 'text-destructive'}`}>
                {saveStatus.message}
              </span>
            )}
            <Button
              variant="outline"
              size="sm"
              onClick={() => resetMutation.mutate()}
              disabled={resetMutation.isPending || saveMutation.isPending}
            >
              {resetMutation.isPending ? '恢复中...' : '恢复默认'}
            </Button>
            <Button
              size="sm"
              onClick={() => saveMutation.mutate()}
              disabled={saveMutation.isPending || resetMutation.isPending}
            >
              {saveMutation.isPending ? '保存中...' : '保存设置'}
            </Button>
          </>
        }
      />

      <Tabs value={activeTab} onValueChange={setActiveTab} className="max-w-3xl">
        <TabsList>
          <TabsTrigger value="account">账号管理</TabsTrigger>
          <TabsTrigger value="ai">AI 配置</TabsTrigger>
          <TabsTrigger value="creative">创作设置</TabsTrigger>
          <TabsTrigger value="ui">界面偏好</TabsTrigger>
        </TabsList>

        <TabsContent value="account" className="mt-4">
          <AccountTab />
        </TabsContent>

        <TabsContent value="ai" className="mt-4">
          <AiConfigTab
            form={form}
            update={update}
            presets={presets}
            samePreset={samePreset}
            applyPreset={applyPreset}
            hasCustomConfig={hasCustomConfig}
            canTestConnection={canTestConnection}
            onTestConnection={() => testMutation.mutate()}
            isTestPending={testMutation.isPending}
            testResult={testResult}
            validationErrors={validationErrors}
            llmHealth={llmHealth}
            isLlmHealthFetching={isLlmHealthFetching}
            onRefreshLlmHealth={() => void refetchLlmHealth()}
            hasUnsavedChanges={hasUnsavedChanges}
          />
        </TabsContent>

        <TabsContent value="creative" className="mt-4">
          <CreativeTab form={form} update={update} />
        </TabsContent>

        <TabsContent value="ui" className="mt-4">
          <UiTab form={form} update={update} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
