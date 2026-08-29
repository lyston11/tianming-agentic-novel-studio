import { api, buildStableIdempotencyKey, get } from './client';
import type {
  MaterialContentResponse,
  MaterialListResponse,
  MaterialResponse,
  UploadMaterialResponse,
} from './types';

export const listMaterials = async (projectId: string): Promise<MaterialListResponse> => {
  const materials = await get<MaterialResponse[]>(`/materials?projectId=${encodeURIComponent(projectId)}`);
  return { materials, totalCount: materials.length };
};

export const getMaterialById = (id: string) =>
  get<MaterialResponse>(`/materials/${id}`);

export const getMaterialContent = (id: string) =>
  get<MaterialContentResponse>(`/materials/${id}/content`);

export const uploadMaterial = async (projectId: string, file: File, category: string, tags?: string) => {
  const formData = new FormData();
  formData.append('File', file);
  formData.append('ProjectId', projectId);
  formData.append('Title', file.name);
  formData.append('Category', category);
  if (tags) formData.append('Tags', tags);

  return api<UploadMaterialResponse>('/materials/upload', {
    method: 'POST',
    headers: {
      'Idempotency-Key': buildStableIdempotencyKey('material-upload', {
        projectId,
        fileName: file.name,
        size: file.size,
        lastModified: file.lastModified,
        category,
        tags,
      }),
    },
    body: formData,
  });
};

export const createMaterialFromText = (req: { projectId: string; title: string; content: string; category: string; tags?: string }) =>
  api<MaterialResponse>('/materials', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('material', req) },
    body: JSON.stringify(req),
  });

export const updateMaterialById = (id: string, req: { title?: string; category?: string; tags?: string }) =>
  api<MaterialResponse>(`/materials/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteMaterialById = (id: string) =>
  api<void>(`/materials/${id}`, { method: 'DELETE' });
