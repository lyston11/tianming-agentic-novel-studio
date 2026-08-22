import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import '../styles/auth.css';

export default function RegisterPage() {
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const navigate = useNavigate();
  const { register, registerLoading, registerError, isAuthenticated } = useAuth();

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/', { replace: true });
    }
  }, [isAuthenticated, navigate]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    register({ username, email, password });
  };

  return (
    <div className="auth-container">
      <div className="auth-card">
        <h1 className="auth-title">创建账户</h1>

        {registerError && (
          <div className="auth-error">
            {registerError.message || '注册失败，请重试'}
          </div>
        )}

        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-input-group">
            <label className="auth-label" htmlFor="username">
              用户名
            </label>
            <input
              id="username"
              type="text"
              className="auth-input"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="输入用户名"
              disabled={registerLoading}
              required
            />
          </div>

          <div className="auth-input-group">
            <label className="auth-label" htmlFor="email">
              邮箱
            </label>
            <input
              id="email"
              type="email"
              className="auth-input"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="输入邮箱地址"
              disabled={registerLoading}
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
              disabled={registerLoading}
              required
            />
          </div>

          <button type="submit" className="auth-button" disabled={registerLoading}>
            {registerLoading ? '注册中...' : '注册'}
          </button>
        </form>

        <div className="auth-link">
          已有账户？<a href="/login">登录</a>
        </div>
      </div>
    </div>
  );
}
