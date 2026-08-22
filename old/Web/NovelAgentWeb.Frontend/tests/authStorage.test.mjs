import assert from 'node:assert/strict';
import {
  hasValidAuthSession,
  readTokenFromPersistedAuth,
  sanitizeAuthState,
} from '../src/services/authStorage.ts';

const adminUser = {
  id: 'admin-id',
  username: 'admin',
  email: 'admin@example.com',
  role: 'Admin',
};

assert.equal(
  hasValidAuthSession({ user: adminUser, token: null, isAuthenticated: true }),
  false,
  'user-only persisted auth must not count as authenticated',
);

assert.deepEqual(
  sanitizeAuthState({ user: adminUser, token: null, isAuthenticated: true }),
  { user: null, token: null, isAuthenticated: false },
  'stale persisted auth must be cleared when token is missing',
);

assert.equal(
  readTokenFromPersistedAuth(JSON.stringify({
    state: { user: adminUser, token: 'token-1', isAuthenticated: true },
    version: 1,
  })),
  'token-1',
  'valid Zustand auth-storage must expose the token',
);

assert.equal(
  readTokenFromPersistedAuth(JSON.stringify({
    state: { user: adminUser, isAuthenticated: true },
    version: 1,
  })),
  null,
  'Zustand auth-storage without token must not expose an auth token',
);
