import { BrowserRouter, Routes, Route, useNavigate } from 'react-router-dom';
import { useEffect } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { getSettings } from './api';
import { AUTH_UNAUTHORIZED_EVENT, hasValidAuthSession } from './services/authStorage';
import { useAuthStore } from './stores/authStore';
import { useProjectStore } from './stores/useProjectStore';
import Rail from './components/layout/Rail';
import ProtectedRoute from './components/ProtectedRoute';
import AgentPage from './pages/AgentPage';
import GoalConsolePage from './pages/agent/GoalConsolePage';
import MaterialsPage from './pages/MaterialsPage';
import WorkflowPage from './pages/WorkflowPage';
import LibraryPage from './pages/LibraryPage';
import SettingsPage from './pages/SettingsPage';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

function AppLayout() {
  const { user, token, isAuthenticated: storedIsAuthenticated } = useAuthStore();
  const isAuthenticated = hasValidAuthSession({ user, token, isAuthenticated: storedIsAuthenticated });
  const { data: settings } = useQuery({
    queryKey: ['settings'],
    queryFn: getSettings,
    enabled: isAuthenticated,
  });
  const initializeFromStorage = useProjectStore((s) => s.initializeFromStorage);

  useEffect(() => {
    initializeFromStorage();
  }, [initializeFromStorage]);

  useEffect(() => {
    if (!settings) return;
    document.documentElement.dataset.theme = settings.theme || 'dark';
    document.documentElement.lang = settings.language || 'zh-CN';
  }, [settings]);

  return (
    <div className="studio-shell">
      <Rail />
      <main className="desk">
        <Routes>
          <Route path="/" element={<AgentPage />} />
          <Route path="/agent" element={<AgentPage />} />
          <Route path="/goal" element={<GoalConsolePage />} />
          <Route path="/goal/:goalId" element={<GoalConsolePage />} />
          <Route path="/materials" element={<MaterialsPage />} />
          <Route path="/workflow" element={<WorkflowPage />} />
          <Route path="/workflow/:projectId" element={<WorkflowPage />} />
          <Route path="/library" element={<LibraryPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
      </main>
    </div>
  );
}

function AuthSessionBoundary() {
  const clearAuth = useAuthStore((s) => s.clearAuth);
  const navigate = useNavigate();

  useEffect(() => {
    const handleUnauthorized = () => {
      clearAuth();
      queryClient.clear();
      navigate('/login', { replace: true });
    };

    window.addEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
    return () => window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
  }, [clearAuth, navigate]);

  return null;
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthSessionBoundary />
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/*" element={<ProtectedRoute><AppLayout /></ProtectedRoute>} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
