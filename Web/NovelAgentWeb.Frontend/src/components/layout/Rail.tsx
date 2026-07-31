import { NavLink, useNavigate } from 'react-router-dom';
import { useEffect } from 'react';
import { hasValidAuthSession } from '../../services/authStorage';
import { useAuthStore } from '../../stores/authStore';
import { useProjectStore } from '../../stores/useProjectStore';
import { projectService, toNovelProjectInfo } from '../../services/projectService';
import { useQuery } from '@tanstack/react-query';

const navItems = [
  { path: '/', label: 'Agent 对话', num: '01' },
  { path: '/materials', label: '创意知识库', num: '02' },
  { path: '/goal', label: 'Goal 工作台', num: '03' },
  { path: '/workflow', label: '创作工作流', num: '04' },
  { path: '/library', label: '小说书城', num: '05' },
  { path: '/settings', label: '用户设置', num: '06' },
];

export default function Rail() {
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
  const ensureProjectSelected = useProjectStore((s) => s.ensureProjectSelected);
  const { user, token, isAuthenticated: storedIsAuthenticated, clearAuth } = useAuthStore();
  const isAuthenticated = hasValidAuthSession({ user, token, isAuthenticated: storedIsAuthenticated });
  const { data: projects, isLoading, isError } = useQuery({
    queryKey: ['projects'],
    queryFn: () => projectService.listProjects(),
    enabled: isAuthenticated,
    staleTime: 5 * 60 * 1000,
  });
  const currentProject = projects?.find((p) => p.id === currentProjectId);
  const navigate = useNavigate();

  useEffect(() => {
    if (projects) ensureProjectSelected(projects.map(toNovelProjectInfo));
  }, [ensureProjectSelected, projects]);

  const handleLogout = () => {
    clearAuth();
    navigate('/login');
  };

  return (
    <aside className="rail">
      <div className="brand-mark">
        <div className="seal">命</div>
        <div>
          <strong>天命</strong>
          <small>Novel Agent</small>
        </div>
      </div>

      <nav>
        <ul className="rail-nav">
          {navItems.map((item) => (
            <li key={item.path}>
              <NavLink
                to={item.path}
                end={item.path === '/'}
                className={({ isActive }) => `stage-item${isActive ? ' active' : ''}`}
              >
                <span className="num">{item.num}</span>
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      <div className="rail-footer">
        <div>
          {isLoading ? '加载中...' :
           isError ? '加载项目失败' :
           currentProjectId && !currentProject ? '项目不存在' :
           currentProject?.title ?? '选择项目...'}
        </div>
        {user && (
          <div style={{ marginTop: '8px', fontSize: '12px', opacity: 0.7 }}>
            {user.username}
          </div>
        )}
        <button
          onClick={handleLogout}
          style={{
            marginTop: '8px',
            width: '100%',
            padding: '6px',
            fontSize: '12px',
            background: 'transparent',
            border: '1px solid rgba(255,255,255,0.2)',
            color: 'inherit',
            cursor: 'pointer',
            borderRadius: '4px'
          }}
        >
          退出登录
        </button>
      </div>
    </aside>
  );
}
