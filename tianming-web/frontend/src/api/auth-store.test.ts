import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  AUTH_STORAGE_KEY,
  AUTH_UNAUTHORIZED_EVENT,
  clearStoredAuthSession,
  getStoredAuthSnapshot,
  hasValidAuthSession,
  notifyUnauthorizedSession,
  sanitizeAuthState,
  setAuthSession,
  subscribeToAuth,
} from './auth-store';

const validUser = { id: 'u1', username: 'writer', email: 'w@t.cn', role: 'User' };

afterEach(() => {
  clearStoredAuthSession();
});

describe('auth-store persistence compatibility', () => {
  it('reads the legacy zustand persist envelope', () => {
    localStorage.setItem(
      AUTH_STORAGE_KEY,
      JSON.stringify({
        state: { user: validUser, token: 'legacy-token', isAuthenticated: true },
        version: 1,
      }),
    );
    // Force a re-read through sanitize on a fresh snapshot read.
    expect(hasValidAuthSession(JSON.parse(localStorage.getItem(AUTH_STORAGE_KEY)!))).toBe(true);
    expect(sanitizeAuthState(JSON.parse(localStorage.getItem(AUTH_STORAGE_KEY)!)).token).toBe('legacy-token');
  });

  it('writes the same envelope shape for cross-grade sessions', () => {
    setAuthSession({ token: 'new-token', user: validUser });
    const stored = JSON.parse(localStorage.getItem(AUTH_STORAGE_KEY)!);
    expect(stored.version).toBe(1);
    expect(stored.state.token).toBe('new-token');
    expect(stored.state.user.username).toBe('writer');
    expect(getStoredAuthSnapshot().isAuthenticated).toBe(true);
  });

  it('rejects malformed payloads', () => {
    expect(sanitizeAuthState({ user: { id: 'x' }, token: 't' })).toEqual({
      user: null,
      token: null,
      isAuthenticated: false,
    });
    localStorage.setItem(AUTH_STORAGE_KEY, '{broken json');
    expect(hasValidAuthSession(null)).toBe(false);
  });
});

describe('auth-store session lifecycle', () => {
  it('notifies listeners on every state change', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeToAuth(listener);

    setAuthSession({ token: 't', user: validUser });
    expect(listener).toHaveBeenCalledTimes(1);

    unsubscribe();
    setAuthSession({ token: 't2', user: validUser });
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('clears storage and broadcasts on unauthorized', () => {
    setAuthSession({ token: 't', user: validUser });
    const listener = vi.fn();
    window.addEventListener(AUTH_UNAUTHORIZED_EVENT, listener);

    notifyUnauthorizedSession();

    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull();
    expect(getStoredAuthSnapshot().isAuthenticated).toBe(false);
    expect(listener).toHaveBeenCalledTimes(1);
    window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, listener);
  });
});
