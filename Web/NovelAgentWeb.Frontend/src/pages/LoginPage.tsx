import { useState, useEffect } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import '../styles/auth.css';

export default function LoginPage() {
  const [emailOrUsername, setEmailOrUsername] = useState('');
  const [password, setPassword] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { login, loginLoading, loginError, isAuthenticated } = useAuth();

  const from = (location.state as { from?: string })?.from || '/';

  useEffect(() => {
    if (isAuthenticated) {
      navigate(from, { replace: true });
    }
  }, [isAuthenticated, navigate, from]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    login({ emailOrUsername, password });
  };

  return (
    <div className="auth-container">
      <div className="auth-card">
        <h1 className="auth-title">登录账户</h1>

        {loginError && (
          <div className="auth-error">
            {loginError.message || '登录失败，请重试'}
          </div>
        )}

        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-input-group">
            <label className="auth-label" htmlFor="emailOrUsername">
              用户名或邮箱
            </label>
            <input
              id="emailOrUsername"
              type="text"
              className="auth-input"
              value={emailOrUsername}
              onChange={(e) => setEmailOrUsername(e.target.value)}
              placeholder="输入用户名或邮箱"
              disabled={loginLoading}
              required
            />
          </div>

          <div className="auth-input-group">
            <label className="auth-label" htmlFor="password">
              密码
            </label>
            <input
              id="password"
              type="password"
              className="auth-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="输入密码"
              disabled={loginLoading}
              required
            />
          </div>

          <button type="submit" className="auth-button" disabled={loginLoading}>
            {loginLoading ? '登录中...' : '登录'}
          </button>
        </form>

        <div className="auth-link">
          还没有账户？<a href="/register">注册</a>
        </div>
      </div>
    </div>
  );
}
