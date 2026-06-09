import { useState, useEffect } from 'react';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';

export default function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { login, loginLoading, loginError, isAuthenticated } = useAuth();

  const [emailOrUsername, setEmailOrUsername] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(true);
  const [validationError, setValidationError] = useState('');

  const from = (location.state as { from?: string })?.from || '/';

  useEffect(() => {
    if (isAuthenticated) {
      navigate(from, { replace: true });
    }
  }, [isAuthenticated, navigate, from]);

  const validateForm = (): boolean => {
    if (!emailOrUsername.trim()) {
      setValidationError('Email or username is required');
      return false;
    }
    if (!password) {
      setValidationError('Password is required');
      return false;
    }
    if (password.length < 8) {
      setValidationError('Password must be at least 8 characters');
      return false;
    }
    setValidationError('');
    return true;
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();

    if (!validateForm()) {
      return;
    }

    login({ emailOrUsername, password });
  };

  const getErrorMessage = (error: Error | null): string => {
    if (!error) return '';

    const message = error.message;

    if (message.includes('401') || message.includes('Unauthorized')) {
      return 'Invalid email/username or password';
    }
    if (message.includes('404') || message.includes('Not Found')) {
      return 'User not found';
    }
    if (message.includes('Network') || message.includes('Failed to fetch')) {
      return 'Network error. Please check your connection.';
    }

    return 'Login failed. Please try again.';
  };

  return (
    <div className="auth-page">
      <div className="auth-container">
        <div className="auth-card">
          <h1>Welcome Back</h1>
          <p className="auth-subtitle">Sign in to continue to your novel workspace</p>

          <form onSubmit={handleSubmit} className="auth-form">
            {(validationError || loginError) && (
              <div className="auth-error">
                {validationError || getErrorMessage(loginError)}
              </div>
            )}

            <div className="form-group">
              <label htmlFor="emailOrUsername">Email or Username</label>
              <input
                id="emailOrUsername"
                type="text"
                value={emailOrUsername}
                onChange={(e) => setEmailOrUsername(e.target.value)}
                placeholder="Enter your email or username"
                disabled={loginLoading}
                autoComplete="username"
              />
            </div>

            <div className="form-group">
              <label htmlFor="password">Password</label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter your password"
                disabled={loginLoading}
                autoComplete="current-password"
              />
            </div>

            <div className="form-group-inline">
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  checked={rememberMe}
                  onChange={(e) => setRememberMe(e.target.checked)}
                  disabled={loginLoading}
                />
                <span>Remember me</span>
              </label>
            </div>

            <button
              type="submit"
              className="auth-button"
              disabled={loginLoading}
            >
              {loginLoading ? 'Signing in...' : 'Sign In'}
            </button>
          </form>

          <div className="auth-footer">
            <p>
              Don't have an account?{' '}
              <Link to="/register">Create one</Link>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
