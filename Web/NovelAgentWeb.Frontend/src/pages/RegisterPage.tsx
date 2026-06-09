import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';

export default function RegisterPage() {
  const navigate = useNavigate();
  const { register, registerLoading, registerError, isAuthenticated } = useAuth();

  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [validationError, setValidationError] = useState('');

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/', { replace: true });
    }
  }, [isAuthenticated, navigate]);

  const validateEmail = (email: string): boolean => {
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return emailRegex.test(email);
  };

  const getPasswordStrength = (password: string): { strength: string; color: string } => {
    if (password.length === 0) return { strength: '', color: '' };
    if (password.length < 8) return { strength: 'Too Short', color: 'red' };

    let score = 0;
    if (password.length >= 12) score++;
    if (/[a-z]/.test(password)) score++;
    if (/[A-Z]/.test(password)) score++;
    if (/[0-9]/.test(password)) score++;
    if (/[^a-zA-Z0-9]/.test(password)) score++;

    if (score <= 2) return { strength: 'Weak', color: 'orange' };
    if (score <= 3) return { strength: 'Medium', color: 'yellow' };
    return { strength: 'Strong', color: 'green' };
  };

  const validateForm = (): boolean => {
    if (!username.trim()) {
      setValidationError('Username is required');
      return false;
    }
    if (username.length < 3) {
      setValidationError('Username must be at least 3 characters');
      return false;
    }
    if (!/^[a-zA-Z0-9_-]+$/.test(username)) {
      setValidationError('Username can only contain letters, numbers, hyphens, and underscores');
      return false;
    }
    if (!email.trim()) {
      setValidationError('Email is required');
      return false;
    }
    if (!validateEmail(email)) {
      setValidationError('Please enter a valid email address');
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
    if (password !== confirmPassword) {
      setValidationError('Passwords do not match');
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

    register({ username, email, password });
  };

  const getErrorMessage = (error: Error | null): string => {
    if (!error) return '';

    const message = error.message;

    if (message.includes('409') || message.includes('Conflict')) {
      return 'Username or email already exists';
    }
    if (message.includes('400') || message.includes('Bad Request')) {
      return 'Invalid registration data. Please check your inputs.';
    }
    if (message.includes('Network') || message.includes('Failed to fetch')) {
      return 'Network error. Please check your connection.';
    }

    return 'Registration failed. Please try again.';
  };

  const passwordStrength = getPasswordStrength(password);

  return (
    <div className="auth-page">
      <div className="auth-container">
        <div className="auth-card">
          <h1>Create Account</h1>
          <p className="auth-subtitle">Join us to start creating your novel</p>

          <form onSubmit={handleSubmit} className="auth-form">
            {(validationError || registerError) && (
              <div className="auth-error">
                {validationError || getErrorMessage(registerError)}
              </div>
            )}

            <div className="form-group">
              <label htmlFor="username">Username</label>
              <input
                id="username"
                type="text"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="Choose a username"
                disabled={registerLoading}
                autoComplete="username"
              />
            </div>

            <div className="form-group">
              <label htmlFor="email">Email</label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="Enter your email"
                disabled={registerLoading}
                autoComplete="email"
              />
            </div>

            <div className="form-group">
              <label htmlFor="password">Password</label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Create a password"
                disabled={registerLoading}
                autoComplete="new-password"
              />
              {password && (
                <div className="password-strength">
                  <span className="strength-label">Strength:</span>
                  <span className={`strength-indicator strength-${passwordStrength.color}`}>
                    {passwordStrength.strength}
                  </span>
                </div>
              )}
            </div>

            <div className="form-group">
              <label htmlFor="confirmPassword">Confirm Password</label>
              <input
                id="confirmPassword"
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                placeholder="Confirm your password"
                disabled={registerLoading}
                autoComplete="new-password"
              />
            </div>

            <button
              type="submit"
              className="auth-button"
              disabled={registerLoading}
            >
              {registerLoading ? 'Creating Account...' : 'Create Account'}
            </button>
          </form>

          <div className="auth-footer">
            <p>
              Already have an account?{' '}
              <Link to="/login">Sign in</Link>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
