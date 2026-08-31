import { api } from './client';
import { AUTH_STORAGE_KEY, clearStoredAuthSession } from './auth-store';
import type {
  AuthResponse,
  LoginRequest,
  RegisterRequest,
} from './auth-store';

export type { AuthResponse, LoginRequest, RegisterRequest, AuthUser } from './auth-store';

export const authApi = {
  register(data: RegisterRequest): Promise<AuthResponse> {
    return api<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
    });
  },

  login(data: LoginRequest): Promise<AuthResponse> {
    return api<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
    });
  },

  logout(): void {
    clearStoredAuthSession();
  },
};

export { AUTH_STORAGE_KEY };
