import { api, buildStableIdempotencyKey, get } from './client';
import type {
  ProjectWorkflowDocument,
  VolumeArcResponse,
} from './types';

export const listVolumeArcs = (projectId: string) =>
  get<VolumeArcResponse[]>(`/workflow/volumes?projectId=${encodeURIComponent(projectId)}`);

export const getProjectWorkflow = (projectId: string) =>
  get<ProjectWorkflowDocument>(`/workflow/project/${encodeURIComponent(projectId)}`);

export const getVolumeArc = (id: string) =>
  get<VolumeArcResponse>(`/workflow/volumes/${id}`);

export const createVolumeArc = (req: { projectId: string; volumeNumber: number; volumeTitle: string; volumeTheme?: string; targetChapters?: number; act1Setup?: string; act2Confrontation?: string; act3Climax?: string; act4Resolution?: string; keyEvents?: string; majorConflict?: string; conflictEscalation?: string }) =>
  api<VolumeArcResponse>('/workflow/volumes', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('volume-arc', req) },
    body: JSON.stringify(req),
  });

export const updateVolumeArc = (id: string, req: { volumeTitle?: string; volumeTheme?: string; targetChapters?: number; currentChapters?: number; act1Setup?: string; act2Confrontation?: string; act3Climax?: string; act4Resolution?: string; keyEvents?: string; majorConflict?: string; conflictEscalation?: string; status?: string }) =>
  api<VolumeArcResponse>(`/workflow/volumes/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteVolumeArc = (id: string) =>
  api<void>(`/workflow/volumes/${id}`, { method: 'DELETE' });
