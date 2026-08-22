import type { AuthUser } from './authService';

export const AUTH_STORAGE_KEY = 'auth-storage';
export const AUTH_UNAUTHORIZED_EVENT = 'auth:unauthorized';

export type StoredAuthState = {
  user: AuthUser | null;
  token: string | null;
  isAuthenticated: boolean;
};

export const emptyAuthState = (): StoredAuthState => ({
  user: null,
  token: null,
  isAuthenticated: false,
});

const isRecord = (value: unknown): value is Record<string, unknown> =>
  Boolean(value) && typeof value === 'object' && !Array.isArray(value);

const asNonEmptyString = (value: unknown): string | null => {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
};

const readStatePayload = (value: unknown): Record<string, unknown> => {
  if (!isRecord(value)) return {};
  const nested = value.state;
  return isRecord(nested) ? nested : value;
};

const readAuthUser = (value: unknown): AuthUser | null => {
  if (!isRecord(value)) return null;

  const id = asNonEmptyString(value.id);
  const username = asNonEmptyString(value.username);
  const email = asNonEmptyString(value.email);
  const role = asNonEmptyString(value.role);

  if (!id || !username || !email || !role) {
    return null;
  }

  return { id, username, email, role };
};

export function sanitizeAuthState(value: unknown): StoredAuthState {
  const state = readStatePayload(value);
  const user = readAuthUser(state.user);
  const token = asNonEmptyString(state.token);

  if (!user || !token) {
    return emptyAuthState();
  }

  return {
    user,
    token,
    isAuthenticated: true,
  };
}

export function hasValidAuthSession(value: unknown): boolean {
  return sanitizeAuthState(value).isAuthenticated;
}

export function readTokenFromPersistedAuth(serialized: string | null): string | null {
  if (!serialized) return null;

  try {
    return sanitizeAuthState(JSON.parse(serialized)).token;
  } catch {
    return null;
  }
}

export function readStoredAuthToken(): string | null {
  if (typeof localStorage === 'undefined') return null;
  return readTokenFromPersistedAuth(localStorage.getItem(AUTH_STORAGE_KEY));
}

export function clearStoredAuthSession(): void {
  if (typeof localStorage === 'undefined') return;
  localStorage.removeItem(AUTH_STORAGE_KEY);
}

export function notifyUnauthorizedSession(): void {
  clearStoredAuthSession();

  if (typeof window === 'undefined') return;
  window.dispatchEvent(new Event(AUTH_UNAUTHORIZED_EVENT));
}
