import { api } from '../api/client';
import { AUTH_STORAGE_KEY } from './authStorage';

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
    localStorage.removeItem(AUTH_STORAGE_KEY);
  },

};
