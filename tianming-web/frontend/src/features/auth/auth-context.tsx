import { useEffect, useState, type ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';
import { authApi } from '@/api/auth';
import {
  AUTH_UNAUTHORIZED_EVENT,
  getStoredAuthSnapshot,
  hasValidAuthSession,
  setAuthSession,
  subscribeToAuth,
} from '@/api/auth-store';
import {
  AuthContext,
  type AuthContextValue,
} from './auth-context-value';

export function AuthProvider({ children }: { children: ReactNode }) {
  // The module-level auth store is the source of truth; sync it into React
  // state so consumers re-render when a 401 clears the session.
  const [snapshot, setSnapshot] = useState(getStoredAuthSnapshot);

  useEffect(() => subscribeToAuth(() => setSnapshot(getStoredAuthSnapshot())), []);

  useEffect(() => {
    const handleUnauthorized = () => setSnapshot(getStoredAuthSnapshot());
    window.addEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
    return () => window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
  }, []);

  const registerMutation = useMutation({
    mutationFn: authApi.register,
    onSuccess: setAuthSession,
  });

  const loginMutation = useMutation({
    mutationFn: authApi.login,
    onSuccess: setAuthSession,
  });

  const value: AuthContextValue = {
    user: snapshot.user,
    token: snapshot.token,
    isAuthenticated: hasValidAuthSession(snapshot),
    register: (data) => registerMutation.mutate(data),
    login: (data) => loginMutation.mutate(data),
    logout: () => {
      authApi.logout();
      setSnapshot(getStoredAuthSnapshot());
    },
    registerLoading: registerMutation.isPending,
    loginLoading: loginMutation.isPending,
    registerError: registerMutation.error,
    loginError: loginMutation.error,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
