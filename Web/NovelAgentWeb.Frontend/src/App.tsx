import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { useEffect } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { getSettings } from './api';
import Rail from './components/layout/Rail';
import AgentPage from './pages/AgentPage';
import MaterialsPage from './pages/MaterialsPage';
import WorkflowPage from './pages/WorkflowPage';
import LibraryPage from './pages/LibraryPage';
import SettingsPage from './pages/SettingsPage';

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
          <Route path="/" element={<AgentPage />} />
          <Route path="/materials" element={<MaterialsPage />} />
          <Route path="/workflow" element={<WorkflowPage />} />
          <Route path="/library" element={<LibraryPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
      </main>
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppLayout />
      </BrowserRouter>
    </QueryClientProvider>
  );
}
