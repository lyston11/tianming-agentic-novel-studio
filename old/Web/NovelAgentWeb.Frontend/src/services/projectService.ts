import { api } from '../api/client';
import type { NovelProjectInfo } from '../api/types';

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

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface UserStats {
  userId: string;
  username: string;
  projectCount: number;
  totalWordCount: number;
  storageUsedMb: number;
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

export const projectService = {
  /**
   * Get paginated projects for the current user.
   * Authorization header is automatically added by api client.
   */
  async getUserProjects(
    pageNumber: number = 1,
    pageSize: number = 20
  ): Promise<PagedResponse<ProjectResponse>> {
    return api<PagedResponse<ProjectResponse>>(
      `/projects?pageNumber=${pageNumber}&pageSize=${pageSize}`
    );
  },

  /**
   * Get all projects for the current user (unpaginated).
   * Returns a simple array of projects.
   * Authorization header is automatically added by api client.
   */
  async listProjects(): Promise<ProjectResponse[]> {
    const response = await api<PagedResponse<ProjectResponse>>(
      '/projects?pageNumber=1&pageSize=1000'
    );
    return response.items;
  },

  /**
   * Get user statistics (project count, storage usage).
   * Returns derived stats from projects list for now.
   */
  async getUserStats(): Promise<UserStats> {
    // Get all projects to calculate stats
    const response = await api<PagedResponse<ProjectResponse>>(
      '/projects?pageNumber=1&pageSize=1000'
    );

    const totalWordCount = response.items.reduce(
      (sum, project) => sum + project.wordCount,
      0
    );

    // Estimate storage: ~2KB per 1000 words (rough estimate)
    const storageUsedMb = (totalWordCount / 1000) * 2 / 1024;

    return {
      userId: response.items[0]?.userId || '',
      username: '', // Will be filled from authStore
      projectCount: response.totalCount,
      totalWordCount,
      storageUsedMb: Math.round(storageUsedMb * 100) / 100,
    };
  },
};
