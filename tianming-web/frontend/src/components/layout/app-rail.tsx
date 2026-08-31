import { useEffect } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { hasValidAuthSession, listProjects, toNovelProjectInfo } from '@/api';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { BrandMark } from '@/components/shared/brand-mark';
import { ensureProjectSelected, useProjectSelection } from '@/lib/project-store';
import { useAuth } from '@/features/auth/auth-context-value';

const navItems = [
  { path: '/', label: 'Agent 对话', num: '01' },
  { path: '/materials', label: '创意知识库', num: '02' },
  { path: '/workflow', label: '创作工作流', num: '03' },
  { path: '/library', label: '小说书城', num: '04' },
  { path: '/settings', label: '用户设置', num: '05' },
];

export function AppRail() {
  const { currentProjectId } = useProjectSelection();
  const { user, token, isAuthenticated: storedIsAuthenticated, logout } = useAuth();
  const isAuthenticated = hasValidAuthSession({
    user,
    token,
    isAuthenticated: storedIsAuthenticated,
  });
  const navigate = useNavigate();

  const { data: projects, isLoading, isError } = useQuery({
    queryKey: ['projects'],
    queryFn: listProjects,
    enabled: isAuthenticated,
    staleTime: 5 * 60 * 1000,
  });
  const currentProject = projects?.find((p) => p.id === currentProjectId);

  useEffect(() => {
    if (projects) ensureProjectSelected(projects.map(toNovelProjectInfo));
  }, [projects]);

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <aside className="flex w-56 shrink-0 flex-col border-r bg-sidebar">
      <div className="p-4">
        <BrandMark />
      </div>

      <nav className="flex-1 overflow-y-auto px-3 py-2">
        <ul className="space-y-1">
          {navItems.map((item) => (
            <li key={item.path}>
              <NavLink
                to={item.path}
                end={item.path === '/'}
                className={({ isActive }) =>
                  `flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm transition-colors ${
                    isActive
                      ? 'bg-sidebar-accent font-medium text-sidebar-accent-foreground'
                      : 'text-sidebar-foreground/70 hover:bg-sidebar-accent/50 hover:text-sidebar-accent-foreground'
                  }`
                }
              >
                <span className="font-mono text-[10px] text-muted-foreground/70">{item.num}</span>
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      <div className="space-y-2 border-t p-4">
        <div className="text-sm">
          {isLoading ? (
            <Skeleton className="h-4 w-24" />
          ) : isError ? (
            <span className="text-destructive">加载项目失败</span>
          ) : currentProjectId && !currentProject ? (
            <span className="text-muted-foreground">项目不存在</span>
          ) : (
            <span className="font-medium">{currentProject?.title ?? '选择项目...'}</span>
          )}
        </div>
        {user && <div className="text-xs text-muted-foreground">{user.username}</div>}
        <Button variant="outline" size="sm" className="w-full" onClick={handleLogout}>
          退出登录
        </Button>
      </div>
    </aside>
  );
}
