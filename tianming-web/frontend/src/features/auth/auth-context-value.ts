import { createContext, useContext } from 'react';
import type { AuthUser, LoginRequest, RegisterRequest } from '@/api/auth-store';

export interface AuthContextValue {
  user: AuthUser | null;
  token: string | null;
  isAuthenticated: boolean;
  register: (data: RegisterRequest) => void;
  login: (data: LoginRequest) => void;
  logout: () => void;
  registerLoading: boolean;
  loginLoading: boolean;
  registerError: Error | null;
  loginError: Error | null;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth 必须在 AuthProvider 内使用');
  }
  return context;
}
