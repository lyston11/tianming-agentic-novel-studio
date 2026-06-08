import type { ReactNode } from 'react';

interface TopbarProps {
  title: string;
  actions?: ReactNode;
}

export default function Topbar({ title, actions }: TopbarProps) {
  return (
    <div className="topbar">
      <h1>{title}</h1>
      {actions && <div className="topbar-actions">{actions}</div>}
    </div>
  );
}
