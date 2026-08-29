/**
 * Module-level auth state (learngraph pattern — no zustand).
 *
 * Storage compatibility: the legacy frontend persisted zustand's
 * `{state: {...}, version: 1}` envelope under the same key; we keep both the
 * key and the payload shape so an existing session survives the migration.
 */

export const AUTH_STORAGE_KEY = 'auth-storage';
export const AUTH_UNAUTHORIZED_EVENT = 'auth:unauthorized';

export interface AuthUser {
  id: string;
  username: string;
  email: string;
  role: string;
}

export interface AuthResponse {
  token: string;
  user: AuthUser;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
}

export interface LoginRequest {
  emailOrUsername: string;
  password: string;
}

export interface StoredAuthState {
  user: AuthUser | null;
  token: string | null;
  isAuthenticated: boolean;
}

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

let currentState: StoredAuthState = readPersistedState();
const listeners = new Set<() => void>();

function readPersistedState(): StoredAuthState {
  if (typeof localStorage === 'undefined') return emptyAuthState();
  try {
    return sanitizeAuthState(JSON.parse(localStorage.getItem(AUTH_STORAGE_KEY) ?? 'null'));
  } catch {
    return emptyAuthState();
  }
}

function persistState(state: StoredAuthState): void {
  if (typeof localStorage === 'undefined') return;
  if (state.isAuthenticated) {
    localStorage.setItem(
      AUTH_STORAGE_KEY,
      JSON.stringify({ state, version: 1 }),
    );
  } else {
    localStorage.removeItem(AUTH_STORAGE_KEY);
  }
}

function setState(next: StoredAuthState): void {
  currentState = next;
  persistState(next);
  for (const listener of [...listeners]) {
    listener();
  }
}

export function getStoredAuthSnapshot(): StoredAuthState {
  return currentState;
}

export function subscribeToAuth(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function setAuthSession(response: AuthResponse): void {
  setState({
    user: response.user,
    token: response.token,
    isAuthenticated: true,
  });
}

export function clearStoredAuthSession(): void {
  setState(emptyAuthState());
}

export function notifyUnauthorizedSession(): void {
  clearStoredAuthSession();

  if (typeof window === 'undefined') return;
  window.dispatchEvent(new Event(AUTH_UNAUTHORIZED_EVENT));
}
