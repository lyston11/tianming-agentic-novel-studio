import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getWorkspace } from '../../api';
import '../../styles/styles.css';

const navItems = [
  { path: '/', label: 'Agent 对话', num: '01' },
  { path: '/materials', label: '创意知识库', num: '02' },
  { path: '/workflow', label: '创作工作流', num: '03' },
  { path: '/library', label: '小说书城', num: '04' },
];

export default function StudioShell() {
  const location = useLocation();
  const navigate = useNavigate();

  const { data: workspace } = useQuery({ queryKey: ['workspace'], queryFn: getWorkspace });

  return (
    <div className="studio-shell">
      <aside className="rail">
        <div className="brand-mark">
          <div className="seal">命</div>
          <div>
            <strong>天命</strong>
            <small>Novel Agent</small>
          </div>
        </div>

        <nav>
          {navItems.map((item) => (
            <button
              key={item.path}
              className={`stage-item${location.pathname === item.path ? ' active' : ''}`}
              onClick={() => navigate(item.path)}
            >
              <span className="stage-num">{item.num}</span>
              {item.label}
            </button>
          ))}
        </nav>

        <div className="rail-footer">
          <div className="project-label">{workspace?.projectName ?? '加载中...'}</div>
        </div>
      </aside>

      <main className="desk">
        <Outlet />
      </main>
    </div>
  );
}
