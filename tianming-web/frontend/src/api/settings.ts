import { get, post, put } from './client';
import type { LlmConnectionHealth, UserSettings } from './types';

export const getSettings = () => get<UserSettings>('/settings');
export const getLlmConnectionHealth = () => get<LlmConnectionHealth>('/settings/llm-health');
export const saveSettings = (settings: Partial<UserSettings>) =>
  put<UserSettings>('/settings', settings);
export const resetSettings = () =>
  post<UserSettings>('/settings/reset');
export const testConnection = (settings: Partial<UserSettings>) =>
  post<{ success: boolean; statusCode?: number; message: string }>('/settings/test-connection', {
    provider: settings.llmProvider,
    baseUrl: settings.llmBaseUrl,
    apiKey: settings.llmApiKey,
    model: settings.llmModel,
  });
