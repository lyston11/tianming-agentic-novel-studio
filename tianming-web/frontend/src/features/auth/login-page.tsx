import { useEffect, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Spinner } from '@/components/ui/spinner';
import { BrandMark } from '@/components/shared/brand-mark';
import { useAuth } from './auth-context-value';

export default function LoginPage() {
  const [emailOrUsername, setEmailOrUsername] = useState('');
  const [password, setPassword] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { login, loginLoading, loginError, isAuthenticated } = useAuth();

  const from = (location.state as { from?: string } | null)?.from || '/';

  useEffect(() => {
    if (isAuthenticated) {
      navigate(from, { replace: true });
    }
  }, [isAuthenticated, navigate, from]);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    login({ emailOrUsername, password });
  };

  return (
    <main className="grid min-h-dvh place-items-center overflow-y-auto bg-muted/40 p-6">
      <div className="w-full max-w-sm rounded-2xl border bg-card p-8 shadow-lg">
        <BrandMark className="mb-7" />
        <h1 className="font-serif text-2xl font-bold">登录账户</h1>

        {loginError && (
          <div
            role="alert"
            className="mt-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
          >
            {loginError.message || '登录失败，请重试'}
          </div>
        )}

        <form className="mt-5 space-y-4" onSubmit={handleSubmit}>
          <div className="space-y-1.5">
            <Label htmlFor="emailOrUsername">用户名或邮箱</Label>
            <Input
              id="emailOrUsername"
              className="h-10"
              value={emailOrUsername}
              onChange={(event) => setEmailOrUsername(event.target.value)}
              placeholder="输入用户名或邮箱"
              autoComplete="username"
              disabled={loginLoading}
              required
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="password">密码</Label>
            <Input
              id="password"
              type="password"
              className="h-10"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="输入密码"
              autoComplete="current-password"
              disabled={loginLoading}
              required
            />
          </div>

          <Button type="submit" size="lg" className="mt-1 w-full" disabled={loginLoading}>
            {loginLoading && <Spinner />}
            {loginLoading ? '登录中...' : '登录'}
          </Button>
        </form>

        <div className="mt-5 text-center text-sm text-muted-foreground">
          还没有账户？
          <Link to="/register" className="text-primary underline-offset-4 hover:underline">
            注册
          </Link>
        </div>
      </div>
    </main>
  );
}
