import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { useEffect } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { getSettings } from './api';
import Rail from './components/layout/Rail';
import ProtectedRoute from './components/ProtectedRoute';
import AgentPage from './pages/AgentPage';
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
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: getSettings });

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
          <Route path="/" element={<ProtectedRoute><AgentPage /></ProtectedRoute>} />
          <Route path="/materials" element={<ProtectedRoute><MaterialsPage /></ProtectedRoute>} />
          <Route path="/workflow" element={<ProtectedRoute><WorkflowPage /></ProtectedRoute>} />
          <Route path="/library" element={<ProtectedRoute><LibraryPage /></ProtectedRoute>} />
          <Route path="/settings" element={<ProtectedRoute><SettingsPage /></ProtectedRoute>} />
        </Routes>
      </main>
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/*" element={<AppLayout />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
