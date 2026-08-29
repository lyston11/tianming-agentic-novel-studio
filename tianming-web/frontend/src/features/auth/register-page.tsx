import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Spinner } from '@/components/ui/spinner';
import { BrandMark } from '@/components/shared/brand-mark';
import { useAuth } from './auth-context-value';

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

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    register({ username, email, password });
  };

  return (
    <main className="grid min-h-dvh place-items-center overflow-y-auto bg-muted/40 p-6">
      <div className="w-full max-w-sm rounded-2xl border bg-card p-8 shadow-lg">
        <BrandMark className="mb-7" />
        <h1 className="font-serif text-2xl font-bold">创建账户</h1>

        {registerError && (
          <div
            role="alert"
            className="mt-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
          >
            {registerError.message || '注册失败，请重试'}
          </div>
        )}

        <form className="mt-5 space-y-4" onSubmit={handleSubmit}>
          <div className="space-y-1.5">
            <Label htmlFor="username">用户名</Label>
            <Input
              id="username"
              className="h-10"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              placeholder="输入用户名"
              autoComplete="username"
              disabled={registerLoading}
              required
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="email">邮箱</Label>
            <Input
              id="email"
              type="email"
              className="h-10"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="输入邮箱地址"
              autoComplete="email"
              disabled={registerLoading}
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
              autoComplete="new-password"
              disabled={registerLoading}
              required
            />
          </div>

          <Button type="submit" size="lg" className="mt-1 w-full" disabled={registerLoading}>
            {registerLoading && <Spinner />}
            {registerLoading ? '注册中...' : '注册'}
          </Button>
        </form>

        <div className="mt-5 text-center text-sm text-muted-foreground">
          已有账户？
          <Link to="/login" className="text-primary underline-offset-4 hover:underline">
            登录
          </Link>
        </div>
      </div>
    </main>
  );
}
