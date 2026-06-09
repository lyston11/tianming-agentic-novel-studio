import { NavLink, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getWorkspace } from '../../api';
import { useAuthStore } from '../../stores/authStore';

const navItems = [
  { path: '/', label: 'Agent 对话', num: '01' },
  { path: '/materials', label: '创意知识库', num: '02' },
  { path: '/workflow', label: '创作工作流', num: '03' },
  { path: '/library', label: '小说书城', num: '04' },
  { path: '/settings', label: '用户设置', num: '05' },
];

export default function Rail() {
  const { data: workspace } = useQuery({ queryKey: ['workspace'], queryFn: getWorkspace });
  const { user, clearAuth } = useAuthStore();
  const navigate = useNavigate();

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
        <div>{workspace?.projectName ?? '加载中...'}</div>
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
