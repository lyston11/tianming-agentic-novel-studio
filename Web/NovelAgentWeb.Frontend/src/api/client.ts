import { notifyUnauthorizedSession, readStoredAuthToken } from '../services/authStorage';

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';

export class ApiError extends Error {
  status: number;
  statusText: string;
  body: string;

  constructor(status: number, statusText: string, body: string) {
    super(body || `${status} ${statusText}`);
    this.name = 'ApiError';
    this.status = status;
    this.statusText = statusText;
    this.body = body;
  }
}

export type ApiEnvelope<T> = {
  success: boolean;
  data?: T | null;
  error?: {
    code?: string;
    message?: string;
    stage?: string;
    recoverable?: boolean;
    recommendedAction?: string;
    requiresUserDecision?: boolean;
    artifactIds?: string[];
  } | null;
  requestId?: string;
  serverTime?: string;
  apiVersion?: string;
  toolSchemaVersion?: string;
  agentLoopVersion?: string;
  kernelVersion?: string;
};

function isApiEnvelope(value: unknown): value is ApiEnvelope<unknown> {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const record = value as Record<string, unknown>;
  return typeof record.success === 'boolean'
    && typeof record.apiVersion === 'string'
    && typeof record.toolSchemaVersion === 'string'
    && typeof record.agentLoopVersion === 'string'
    && typeof record.kernelVersion === 'string'
    && ('data' in record || 'error' in record);
}

function unwrapEnvelope<T>(value: unknown): T {
  if (!isApiEnvelope(value)) {
    throw new Error('API 响应缺少统一信封');
  }

  if (!value.success) {
    const message = value.error?.message || value.error?.code || 'API 请求失败';
    throw new Error(message);
  }

  return value.data as T;
}

function readEnvelopeError(text: string, fallback: string): string {
  if (!text) {
    return fallback;
  }

  try {
    const parsed = JSON.parse(text);
    if (isApiEnvelope(parsed)) {
      return parsed.error?.message || parsed.error?.code || fallback;
    }
  } catch {
    return text;
  }

  return text;
}

export async function api<T>(path: string, options?: RequestInit): Promise<T> {
  // Get token from localStorage (Zustand persist format)
  const token = readStoredAuthToken();

  const headers: Record<string, string> = {};

  // Only set Content-Type for non-FormData requests
  // (FormData sets its own Content-Type with boundary)
  if (!(options?.body instanceof FormData)) {
    headers['Content-Type'] = 'application/json';
  }

  // Add Authorization header if token exists
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  // Merge with any additional headers from options
  if (options?.headers) {
    Object.entries(options.headers).forEach(([key, value]) => {
      if (typeof value === 'string') {
        headers[key] = value;
      }
    });
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  });
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    if (response.status === 401) {
      notifyUnauthorizedSession();
    }
    throw new ApiError(
      response.status,
      response.statusText,
      readEnvelopeError(text, `${response.status} ${response.statusText}`));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return unwrapEnvelope<T>(JSON.parse(text));
}

export const get = <T>(path: string) => api<T>(path);

export const post = <T>(path: string, body?: unknown) =>
  api<T>(path, {
    method: 'POST',
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
