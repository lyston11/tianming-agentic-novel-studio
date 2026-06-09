import { api } from '../api/client';

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

// Use the same key as Zustand persist for consistency
const TOKEN_KEY = 'auth-storage';

export const authService = {
  async register(data: RegisterRequest): Promise<AuthResponse> {
    return api<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
    });
  },

  async login(data: LoginRequest): Promise<AuthResponse> {
    return api<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
    });
  },

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
  },

  getStoredToken(): string | null {
    try {
      const stored = localStorage.getItem(TOKEN_KEY);
      if (!stored) return null;
      const parsed = JSON.parse(stored);
      return parsed?.state?.token || null;
    } catch {
      return null;
    }
  },

  setStoredToken(_token: string): void {
    // Token is already persisted by Zustand, this is a no-op
    // but kept for API compatibility
  },
};
