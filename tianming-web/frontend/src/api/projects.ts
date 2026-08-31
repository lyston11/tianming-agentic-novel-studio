import { api, buildStableIdempotencyKey, get } from './client';
import type {
  NovelProjectCreateRequest,
  NovelProjectDeleteResult,
  NovelProjectInfo,
  NovelProjectUpdateRequest,
  WorkspaceResponse,
} from './types';

export interface ProjectResponse {
  id: string;
  userId: string;
  title: string;
  genre: string | null;
  subGenre: string | null;
  coreHook: string | null;
  status: string;
  wordCount: number;
  coverImageUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PagedProjectResponse {
  items: ProjectResponse[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export function toNovelProjectInfo(project: ProjectResponse): NovelProjectInfo {
  return {
    id: project.id,
    title: project.title,
    genre: project.genre ?? '',
    subGenre: project.subGenre ?? '',
    coreHook: project.coreHook ?? '',
    readerPromise: '',
    status: project.status,
    coverImageUrl: project.coverImageUrl,
    createdAt: project.createdAt,
    updatedAt: project.updatedAt,
  };
}

/** All projects for the current user (unpaginated). */
export async function listProjects(): Promise<ProjectResponse[]> {
  const response = await api<PagedProjectResponse>('/projects?pageNumber=1&pageSize=1000');
  return response.items;
}

export async function getUserStats(): Promise<{
  projectCount: number;
  totalWordCount: number;
  storageUsedMb: number;
}> {
  const response = await api<PagedProjectResponse>('/projects?pageNumber=1&pageSize=1000');
  const totalWordCount = response.items.reduce((sum, project) => sum + project.wordCount, 0);
  const storageUsedMb = (totalWordCount / 1000) * 2 / 1024;
  return {
    projectCount: response.totalCount,
    totalWordCount,
    storageUsedMb: Math.round(storageUsedMb * 100) / 100,
  };
}

export const createNovelProject = (req: NovelProjectCreateRequest) => {
  const payload = {
    title: req.title || '未命名新书',
    genre: req.genre,
    coreHook: req.seed,
  };
  return api<NovelProjectInfo>('/projects', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('novel-project', payload) },
    body: JSON.stringify(payload),
  });
};

export const activateNovelProject = (projectId: string) =>
  get<NovelProjectInfo>(`/projects/${projectId}`);

export const updateNovelProject = (projectId: string, req: NovelProjectUpdateRequest) =>
  api<NovelProjectInfo>(`/projects/${projectId}`, {
    method: 'PUT',
    body: JSON.stringify(req),
  });

export const deleteNovelProject = async (projectId: string): Promise<NovelProjectDeleteResult> => {
  await api<void>(`/projects/${projectId}`, { method: 'DELETE' });
  return { success: true, message: '项目已删除。', activeProjectId: '' };
};

export const getWorkspace = () => get<WorkspaceResponse>('/workspace');
