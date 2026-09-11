import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { Toaster } from 'sonner';
import { useEffect } from 'react';
import { AUTH_UNAUTHORIZED_EVENT } from '@/api/auth-store';
import { getSettings } from '@/api';
import { AppRail } from '@/components/layout/app-rail';
import { ProtectedRoute } from '@/components/layout/protected-route';
import { AuthProvider } from '@/features/auth/auth-context';
import { Spinner } from '@/components/ui/spinner';
import { applyTheme, persistTheme } from '@/lib/theme';

const LoginPage = lazy(() => import('@/features/auth/login-page'));
const RegisterPage = lazy(() => import('@/features/auth/register-page'));
const AgentPage = lazy(() => import('@/features/agent/agent-page'));
const MaterialsPage = lazy(() => import('@/features/materials/materials-page'));
const WorkflowPage = lazy(() => import('@/features/workflow/workflow-page'));
const LibraryPage = lazy(() => import('@/features/library/library-page'));
const SettingsPage = lazy(() => import('@/features/settings/settings-page'));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

function RouteFallback() {
  return (
    <div className="grid flex-1 place-items-center p-10 text-muted-foreground">
      <Spinner className="size-6" />
    </div>
  );
}

/** Clears the query cache when a 401 expires the session mid-flight. */
function AuthSessionBoundary() {
  const navigate = useNavigate();

  useEffect(() => {
    const handleUnauthorized = () => {
      queryClient.clear();
      navigate('/login', { replace: true });
    };

    window.addEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
    return () => window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
  }, [navigate]);

  return null;
}

/**
 * Applies the theme from the persisted user settings outside the settings page:
 * the settings-page effect only live-previews edits, this component keeps the
 * document class and the localStorage cache tracking the saved value.
 */
function ThemeSync() {
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: getSettings });
  const theme = settings?.theme;

  useEffect(() => {
    if (!theme) return;
    applyTheme(theme);
    persistTheme(theme);
  }, [theme]);

  return null;
}

function AppLayout() {
  return (
    <div className="flex h-dvh overflow-hidden bg-background">
      <ThemeSync />
      <AppRail />
      <main className="flex min-w-0 flex-1 flex-col overflow-y-auto p-5">
        <Suspense fallback={<RouteFallback />}>
          <Routes>
            <Route path="/" element={<AgentPage />} />
            <Route path="/agent" element={<AgentPage />} />
            <Route path="/materials" element={<MaterialsPage />} />
            <Route path="/workflow" element={<WorkflowPage />} />
            <Route path="/workflow/:projectId" element={<WorkflowPage />} />
            <Route path="/library" element={<LibraryPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Routes>
        </Suspense>
      </main>
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <AuthSessionBoundary />
          <Suspense fallback={<RouteFallback />}>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route path="/register" element={<RegisterPage />} />
              <Route
                path="/*"
                element={
                  <ProtectedRoute>
                    <AppLayout />
                  </ProtectedRoute>
                }
              />
            </Routes>
          </Suspense>
        </BrowserRouter>
      </AuthProvider>
      <Toaster position="top-center" richColors />
    </QueryClientProvider>
  );
}
