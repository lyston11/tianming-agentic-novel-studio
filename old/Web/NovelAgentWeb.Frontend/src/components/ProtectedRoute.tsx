import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { hasValidAuthSession } from '../services/authStorage';
import { useAuthStore } from '../stores/authStore';

interface ProtectedRouteProps {
  children: ReactNode;
}

export default function ProtectedRoute({ children }: ProtectedRouteProps) {
  const { user, token, isAuthenticated: storedIsAuthenticated } = useAuthStore();
  const isAuthenticated = hasValidAuthSession({ user, token, isAuthenticated: storedIsAuthenticated });
  const location = useLocation();

  if (!isAuthenticated) {
    // Redirect to login page while preserving the intended destination
    return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  }

  return <>{children}</>;
}
