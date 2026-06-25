import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AuthUser } from '../services/authService';
import { emptyAuthState, sanitizeAuthState } from '../services/authStorage';

interface AuthState {
  user: AuthUser | null;
  token: string | null;
  isAuthenticated: boolean;

  setAuth: (user: AuthUser, token: string) => void;
  clearAuth: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      token: null,
      isAuthenticated: false,

      setAuth: (user, token) => set(sanitizeAuthState({ user, token })),

      clearAuth: () => set(emptyAuthState()),
    }),
    {
      name: 'auth-storage',
      version: 1,
      migrate: (persistedState: any, version: number) => {
        if (version === 0) {
          // Clear old flat structure data
          return emptyAuthState();
        }
        return sanitizeAuthState(persistedState);
      },
      merge: (persistedState, currentState) => {
        const sanitized = sanitizeAuthState(persistedState);
        return {
          ...currentState,
          ...sanitized,
        };
      },
      partialize: (state) => ({
        ...sanitizeAuthState(state),
      }),
    }
  )
);
