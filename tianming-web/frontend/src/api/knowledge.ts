import { api, buildStableIdempotencyKey, get } from './client';
import type {
  KnowledgeDirectoryResponse,
  KnowledgeProcessingTaskResponse,
  KnowledgeResponse,
  KnowledgeSearchResult,
} from './types';

export const searchKnowledgeEntries = (
  req: { projectId: string; query: string; topK?: number; entryType?: string },
  signal?: AbortSignal,
) => api<KnowledgeSearchResult[]>('/knowledge/search', {
  method: 'POST',
  body: JSON.stringify(req),
  signal,
});

export const listKnowledgeEntries = (projectId?: string) =>
  projectId?.trim()
    ? get<KnowledgeResponse[]>(`/knowledge?projectId=${encodeURIComponent(projectId.trim())}`)
    : get<KnowledgeResponse[]>('/knowledge');

export const listKnowledgeDirectories = () =>
  get<KnowledgeDirectoryResponse[]>('/knowledge/directories');

export const createKnowledgeDirectory = (req: { name: string }) =>
  api<KnowledgeDirectoryResponse>('/knowledge/directories', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('knowledge-directory', req) },
    body: JSON.stringify(req),
  });

export const updateKnowledgeDirectory = (key: string, req: { name: string }) =>
  api<KnowledgeDirectoryResponse>(`/knowledge/directories/${encodeURIComponent(key)}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteKnowledgeDirectory = (key: string) =>
  api<void>(`/knowledge/directories/${encodeURIComponent(key)}`, { method: 'DELETE' });

export const createKnowledgeEntry = (req: { projectId: string; title: string; content: string; entryType: string; tags?: string[]; weight?: number }) =>
  api<KnowledgeResponse>('/knowledge', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('knowledge-entry', req) },
    body: JSON.stringify(req),
  });

export const updateKnowledgeEntryById = (id: string, req: { entryType?: string; title?: string; content?: string; tags?: string[]; weight?: number; isArchived?: boolean }) =>
  api<KnowledgeResponse>(`/knowledge/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteKnowledgeEntryById = (id: string) =>
  api<void>(`/knowledge/${id}`, { method: 'DELETE' });

export const uploadKnowledgeFile = (projectId: string, file: File, title?: string) => {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('projectId', projectId);
  if (title) formData.append('title', title);

  return api<{ taskId: string; fileName: string; fileSize: number; status: string; message: string }>('/knowledge/upload', {
    method: 'POST',
    body: formData,
  });
};

export const getKnowledgeTask = (taskId: string) =>
  get<KnowledgeProcessingTaskResponse>(`/knowledge/tasks/${encodeURIComponent(taskId)}`);
