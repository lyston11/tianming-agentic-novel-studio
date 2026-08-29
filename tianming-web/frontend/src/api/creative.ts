import { api, buildStableIdempotencyKey, get } from './client';
import type {
  CreativeIntentItem,
  CreativeIntentQueryResult,
  CreateCreativeIntentRequest,
  DecideCreativeIntentRequest,
} from './types';

export const listCreativeIntents = (projectId: string, status?: string, targetChapterId?: string, limit?: number) => {
  const params = new URLSearchParams({ projectId });
  if (status) params.set('status', status);
  if (targetChapterId) params.set('targetChapterId', targetChapterId);
  if (limit) params.set('limit', String(limit));
  return get<CreativeIntentQueryResult>(`/creative/intents?${params.toString()}`);
};

export const createCreativeIntent = (req: CreateCreativeIntentRequest) =>
  api<CreativeIntentItem>('/creative/intents', {
    method: 'POST',
    headers: { 'Idempotency-Key': buildStableIdempotencyKey('creative-intent', req) },
    body: JSON.stringify(req),
  });

export const decideCreativeIntent = (intentId: string, req: DecideCreativeIntentRequest) =>
  api<CreativeIntentItem>(`/creative/intents/${encodeURIComponent(intentId)}/decision`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });
