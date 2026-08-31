import { api, buildActionIdempotencyKey, buildStableIdempotencyKey, get } from './client';
import type {
  ChapterCreateRequest,
  ChapterResponse,
  ChapterVersionCompareResponse,
  ChapterVersionResponse,
} from './types';

export const listProjectChapters = (projectId: string) =>
  get<ChapterResponse[]>(`/chapters/project/${encodeURIComponent(projectId)}`);

export const createChapter = (req: ChapterCreateRequest) =>
  api<ChapterResponse>('/chapters', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('chapter', req) },
    body: JSON.stringify(req),
  });

export const getChapterById = (chapterId: string) =>
  get<ChapterResponse>(`/chapters/${encodeURIComponent(chapterId)}`);

export const getChapterVersions = (chapterId: string) =>
  get<ChapterVersionResponse[]>(`/chapters/${encodeURIComponent(chapterId)}/versions`);

export const compareChapterVersions = (
  chapterId: string,
  leftVersionId: string,
  rightVersionId: string,
) => {
  const query = new URLSearchParams({
    leftVersionId,
    rightVersionId,
  });
  return get<ChapterVersionCompareResponse>(
    `/chapters/${encodeURIComponent(chapterId)}/versions/compare?${query.toString()}`,
  );
};

export const rollbackChapterVersion = (chapterId: string, versionId: string, reason: string) =>
  api<{
    success: boolean;
    message: string;
    projectId: string;
    chapterId: string;
    currentVersionId: string;
    currentVersionNumber: number;
    currentDocumentId: string;
    runtimeRunId: string;
    invalidatedPackageIds: string[];
  }>(`/chapters/${encodeURIComponent(chapterId)}/versions/${encodeURIComponent(versionId)}/rollback`, {
    method: 'POST',
    headers: { 'Idempotency-Key': buildActionIdempotencyKey('chapter-version-rollback') },
    body: JSON.stringify({ reason }),
  });
