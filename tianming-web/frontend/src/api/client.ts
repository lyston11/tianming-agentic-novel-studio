import {
  getStoredAuthSnapshot,
  notifyUnauthorizedSession,
} from './auth-store';

/**
 * API client for the Tianming web frontend.
 *
 * Contract notes (inherited from the legacy frontend, must not drift):
 * - Every JSON response must carry the unified ApiEnvelope; a response without
 *   one is a hard error.
 * - A failed envelope (`success: false`) surfaces `error.message` as a plain
 *   Error with the original code attached.
 * - 401 clears the stored session and broadcasts AUTH_UNAUTHORIZED_EVENT.
 * - Write endpoints rely on Idempotency-Key headers: stable FNV hashes for
 *   deterministic payloads, action UUIDs for one-shot commands.
 */

export const API_BASE_URL = resolveApiBaseUrl();

function resolveApiBaseUrl(): string {
  const configured = (import.meta.env.VITE_API_BASE_URL ?? '').trim();
  if (configured.length > 0) {
    return configured.replace(/\/+$/, '');
  }
  return '/api';
}

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

export class EnvelopeError extends Error {
  code: string | undefined;

  constructor(message: string, code?: string) {
    super(message);
    this.name = 'EnvelopeError';
    this.code = code;
  }
}

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
    throw new EnvelopeError(message, value.error?.code);
  }

  return value.data as T;
}

function readEnvelopeError(text: string, fallback: string): string {
  if (!text) {
    return fallback;
  }

  try {
    const parsed = JSON.parse(text) as unknown;
    if (isApiEnvelope(parsed)) {
      return parsed.error?.message || parsed.error?.code || fallback;
    }
  } catch {
    return text;
  }

  return text;
}

const RETRYABLE_STATUS = new Set([502, 503, 504]);
const RETRY_DELAYS_MS = [500, 1_000, 2_000];

const delay = (ms: number, signal?: AbortSignal | null): Promise<void> =>
  new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener(
      'abort',
      () => {
        clearTimeout(timer);
        reject(signal.reason instanceof Error ? signal.reason : new Error('Aborted'));
      },
      { once: true },
    );
  });

export async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const token = getStoredAuthSnapshot().token;
  const method = (options?.method ?? 'GET').toUpperCase();
  const canRetry = method === 'GET' || method === 'HEAD';

  const headers: Record<string, string> = {};
  // FormData sets its own multipart Content-Type with the boundary.
  if (!(options?.body instanceof FormData)) {
    headers['Content-Type'] = 'application/json';
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  if (options?.headers) {
    for (const [key, value] of Object.entries(options.headers)) {
      if (typeof value === 'string') {
        headers[key] = value;
      }
    }
  }

  let lastError: unknown;
  for (let attempt = 0; attempt <= RETRY_DELAYS_MS.length; attempt += 1) {
    let response: Response;
    try {
      response = await fetch(`${API_BASE_URL}${path}`, { ...options, headers });
    } catch (error) {
      if (options?.signal?.aborted) throw error;
      lastError = error;
      if (!canRetry || attempt === RETRY_DELAYS_MS.length) {
        throw lastError instanceof Error ? lastError : new Error(String(lastError));
      }
      await delay(RETRY_DELAYS_MS[attempt]!, options?.signal);
      continue;
    }

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      if (response.status === 401) {
        notifyUnauthorizedSession();
      }
      if (canRetry && RETRYABLE_STATUS.has(response.status) && attempt < RETRY_DELAYS_MS.length) {
        lastError = new ApiError(
          response.status,
          response.statusText,
          readEnvelopeError(text, `${response.status} ${response.statusText}`),
        );
        await delay(RETRY_DELAYS_MS[attempt]!, options?.signal);
        continue;
      }
      throw new ApiError(
        response.status,
        response.statusText,
        readEnvelopeError(text, `${response.status} ${response.statusText}`),
      );
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

  throw lastError instanceof Error ? lastError : new Error('API 请求失败');
}

export const get = <T>(path: string, signal?: AbortSignal) => api<T>(path, { signal });

export const post = <T>(path: string, body?: unknown, options?: RequestInit) =>
  api<T>(path, {
    ...options,
    method: 'POST',
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

export const put = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: 'PUT', body: body !== undefined ? JSON.stringify(body) : undefined });

export const patch = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: 'PATCH', body: body !== undefined ? JSON.stringify(body) : undefined });

export const del = <T>(path: string) => api<T>(path, { method: 'DELETE' });

// --- Idempotency keys -------------------------------------------------------

export const buildStableIdempotencyKey = (prefix: string, payload: unknown): string => {
  const stable = stableJson(payload);
  let hash = 2166136261;
  for (let i = 0; i < stable.length; i += 1) {
    hash ^= stable.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }

  return `${prefix}-${(hash >>> 0).toString(16)}`;
};

export const buildActionIdempotencyKey = (prefix: string): string => {
  const randomId = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

  return `${prefix}-${randomId}`;
};

const stableJson = (value: unknown): string => {
  if (Array.isArray(value)) {
    return `[${value.map(stableJson).join(',')}]`;
  }
  if (value && typeof value === 'object') {
    return `{${Object.entries(value as Record<string, unknown>)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, item]) => `${JSON.stringify(key)}:${stableJson(item)}`)
      .join(',')}}`;
  }

  return JSON.stringify(value) ?? 'null';
};

// --- SSE streams ------------------------------------------------------------

export interface SseStreamConnection {
  onopen?: () => void;
  onmessage?: (event: MessageEvent<string>) => void;
  onerror?: () => void;
  close: () => void;
}

const dispatchSseBlock = (connection: SseStreamConnection, block: string) => {
  const data = block
    .split('\n')
    .filter((line) => line.startsWith('data:'))
    .map((line) => line.slice(5).trimStart())
    .join('\n');
  if (!data) return;
  connection.onmessage?.(new MessageEvent('message', { data }));
};

const createFetchSseConnection = (url: string): SseStreamConnection => {
  const token = getStoredAuthSnapshot().token;
  const controller = new AbortController();
  const connection: SseStreamConnection = {
    close: () => controller.abort(),
  };

  void (async () => {
    try {
      const headers: Record<string, string> = {};
      if (token) headers.Authorization = `Bearer ${token}`;

      const response = await fetch(url, {
        headers,
        signal: controller.signal,
        credentials: 'same-origin',
      });
      if (!response.ok || !response.body) {
        throw new Error(`SSE connection failed with status ${response.status}`);
      }

      connection.onopen?.();
      const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
      let buffer = '';
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer = (buffer + value).replace(/\r\n/g, '\n');
        let delimiter = buffer.indexOf('\n\n');
        while (delimiter >= 0) {
          const block = buffer.slice(0, delimiter);
          buffer = buffer.slice(delimiter + 2);
          dispatchSseBlock(connection, block);
          delimiter = buffer.indexOf('\n\n');
        }
      }
      if (!controller.signal.aborted) connection.onerror?.();
    } catch {
      if (!controller.signal.aborted) connection.onerror?.();
    }
  })();

  return connection;
};

/** Legacy goal/agent runtime stream. */
export const createSseConnection = (sessionId: string, afterEventId?: string | null): SseStreamConnection => {
  const params = new URLSearchParams();
  if (afterEventId) params.set('afterEventId', afterEventId);
  const query = params.toString();
  const url = `${API_BASE_URL}/agent/sse/${sessionId}${query ? `?${query}` : ''}`;
  return createFetchSseConnection(url);
};

const createNovelAgentStreamConnection = (
  stream: 'conversations' | 'workflows',
  streamId: string,
  cursor?: string | null,
): SseStreamConnection => {
  const params = new URLSearchParams();
  if (cursor) params.set('cursor', cursor);
  const query = params.toString();
  const url = `${API_BASE_URL}/novel-agent/streams/${stream}/${encodeURIComponent(streamId)}${query ? `?${query}` : ''}`;
  return createFetchSseConnection(url);
};

export const createNovelAgentConversationSseConnection = (sessionId: string, cursor?: string | null) =>
  createNovelAgentStreamConnection('conversations', sessionId, cursor);

export const createNovelAgentWorkflowSseConnection = (projectId: string, cursor?: string | null) =>
  createNovelAgentStreamConnection('workflows', projectId, cursor);
