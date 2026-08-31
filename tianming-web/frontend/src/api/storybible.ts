import { api, buildStableIdempotencyKey, get } from './client';
import type {
  CharacterResponse,
  StoryBibleResponse,
  StoryConstitutionResponse,
} from './types';

export const getStoryBibleByProject = (projectId: string) =>
  get<StoryBibleResponse>(`/storybible?projectId=${encodeURIComponent(projectId)}`);

export const getConstitution = (projectId: string) =>
  get<StoryConstitutionResponse>(`/storybible/constitution?projectId=${encodeURIComponent(projectId)}`);

export const createOrUpdateConstitution = (req: { projectId: string; genre: string; subGenre?: string; coreHook: string; readerPromise?: string; genreProfile?: string; targetAudience?: string; taboos?: string }) =>
  api<StoryConstitutionResponse>('/storybible/constitution', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('story-constitution', req) },
    body: JSON.stringify(req),
  });

export const listCharacters = (projectId: string) =>
  get<CharacterResponse[]>(`/storybible/characters?projectId=${encodeURIComponent(projectId)}`);

export const createCharacter = (req: { projectId: string; name: string; role: string; alias?: string; age?: number; gender?: string; appearance?: string; personality?: string; background?: string; coreGoal?: string; motivation?: string }) =>
  api<CharacterResponse>('/storybible/characters', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('character', req) },
    body: JSON.stringify(req),
  });

export const updateCharacterById = (id: string, req: { name?: string; role?: string; alias?: string; age?: number; gender?: string; appearance?: string; personality?: string; background?: string; coreGoal?: string; motivation?: string }) =>
  api<CharacterResponse>(`/storybible/characters/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteCharacterById = (id: string) =>
  api<void>(`/storybible/characters/${id}`, { method: 'DELETE' });
