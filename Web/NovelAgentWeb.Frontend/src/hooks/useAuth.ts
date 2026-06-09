import { useMutation } from '@tanstack/react-query';
import { authService } from '../services/authService';
import { useAuthStore } from '../stores/authStore';
import type { LoginRequest, RegisterRequest } from '../services/authService';

export function useAuth() {
  const { user, token, isAuthenticated, setAuth, clearAuth } = useAuthStore();

  const registerMutation = useMutation({
    mutationFn: (data: RegisterRequest) => authService.register(data),
    onSuccess: (response) => {
      // Zustand persist middleware handles localStorage automatically
      setAuth(response.user, response.token);
    },
  });

  const loginMutation = useMutation({
    mutationFn: (data: LoginRequest) => authService.login(data),
    onSuccess: (response) => {
      // Zustand persist middleware handles localStorage automatically
      setAuth(response.user, response.token);
    },
  });

  const logout = () => {
    authService.logout();
    clearAuth();
  };

  return {
    user,
    token,
    isAuthenticated,
    register: registerMutation.mutate,
    login: loginMutation.mutate,
    logout,
    registerLoading: registerMutation.isPending,
    loginLoading: loginMutation.isPending,
    registerError: registerMutation.error,
    loginError: loginMutation.error,
  };
}
