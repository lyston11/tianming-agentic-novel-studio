import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, buildStableIdempotencyKey, EnvelopeError } from './client';
import { clearStoredAuthSession, setAuthSession } from './auth-store';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function envelope(data: unknown) {
  return {
    success: true,
    data,
    apiVersion: '1.0.0',
    toolSchemaVersion: '1.0.0',
    agentLoopVersion: '1.0.0',
    kernelVersion: '1.0.0',
  };
}

const fetchMock = () => {
  const mock = vi.fn();
  vi.stubGlobal('fetch', mock);
  return mock;
};

afterEach(() => {
  vi.unstubAllGlobals();
  clearStoredAuthSession();
});

describe('api() envelope contract', () => {
  it('unwraps a unified envelope and returns data', async () => {
    fetchMock().mockResolvedValue(jsonResponse(200, envelope({ hello: '天命' })));
    await expect(api<{ hello: string }>('/ping')).resolves.toEqual({ hello: '天命' });
  });

  it('rejects a response without the unified envelope', async () => {
    fetchMock().mockResolvedValue(jsonResponse(200, { foo: 1 }));
    await expect(api('/ping')).rejects.toThrow('API 响应缺少统一信封');
  });

  it('surfaces envelope business errors with their message', async () => {
    fetchMock().mockResolvedValue(jsonResponse(200, {
      ...envelope(null),
      success: false,
      error: { code: 'goal_locked', message: 'Goal 已锁定，无法修改' },
    }));
    const error = await api('/ping').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(EnvelopeError);
    expect((error as EnvelopeError).message).toBe('Goal 已锁定，无法修改');
    expect((error as EnvelopeError).code).toBe('goal_locked');
  });

  it('throws ApiError with the envelope message on HTTP failure', async () => {
    fetchMock().mockResolvedValue(jsonResponse(404, {
      success: false,
      error: { code: 'not_found', message: '资源不存在' },
      apiVersion: '1.0.0',
      toolSchemaVersion: '1.0.0',
      agentLoopVersion: '1.0.0',
      kernelVersion: '1.0.0',
    }));
    const error = await api('/ping').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
    expect((error as ApiError).body).toBe('资源不存在');
  });

  it('clears the session and notifies on 401', async () => {
    setAuthSession({
      token: 'token-1',
      user: { id: 'u1', username: 'writer', email: 'w@t.cn', role: 'User' },
    });
    const listener = vi.fn();
    window.addEventListener('auth:unauthorized', listener);
    fetchMock().mockResolvedValue(new Response('expired', { status: 401 }));

    await expect(api('/ping')).rejects.toBeInstanceOf(ApiError);
    expect(listener).toHaveBeenCalledTimes(1);
    window.removeEventListener('auth:unauthorized', listener);
  });

  it('retries GET on 503 with backoff and then succeeds', async () => {
    const fetchFn = vi.fn()
      .mockResolvedValueOnce(new Response('bad gateway', { status: 503 }))
      .mockResolvedValueOnce(jsonResponse(200, envelope('ok')));
    vi.stubGlobal('fetch', fetchFn);
    vi.useFakeTimers();
    const pending = api<string>('/ping');
    await vi.advanceTimersByTimeAsync(1_500);
    await expect(pending).resolves.toBe('ok');
    expect(fetchFn).toHaveBeenCalledTimes(2);
    vi.useRealTimers();
  });

  it('does not retry POST on 503', async () => {
    const fetchFn = vi.fn().mockResolvedValue(new Response('bad gateway', { status: 503 }));
    vi.stubGlobal('fetch', fetchFn);
    await expect(api('/ping', { method: 'POST' })).rejects.toBeInstanceOf(ApiError);
    expect(fetchFn).toHaveBeenCalledTimes(1);
  });

  it('sends the stored bearer token and skips Content-Type for FormData', async () => {
    setAuthSession({
      token: 'token-2',
      user: { id: 'u1', username: 'writer', email: 'w@t.cn', role: 'User' },
    });
    const fetchFn = vi.fn().mockResolvedValue(jsonResponse(200, envelope(null)));
    vi.stubGlobal('fetch', fetchFn);

    const form = new FormData();
    await api('/materials/upload', { method: 'POST', body: form });

    const [, init] = fetchFn.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers['Authorization']).toBe('Bearer token-2');
    expect(headers['Content-Type']).toBeUndefined();
  });
});

describe('idempotency keys', () => {
  it('is stable for equal payloads regardless of key order', () => {
    expect(buildStableIdempotencyKey('p', { a: 1, b: 'x' })).toBe(
      buildStableIdempotencyKey('p', { b: 'x', a: 1 }),
    );
    expect(buildStableIdempotencyKey('p', { a: 1 })).not.toBe(
      buildStableIdempotencyKey('p', { a: 2 }),
    );
  });
});
