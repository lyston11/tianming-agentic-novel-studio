import type { ReactNode } from 'react';

interface TopbarProps {
  title?: string;
  leading?: ReactNode;
  center?: ReactNode;
  actions?: ReactNode;
  className?: string;
}

export default function Topbar({ title, leading, center, actions, className }: TopbarProps) {
  return (
    <div className={['topbar', className].filter(Boolean).join(' ')}>
      {leading ? <div className="topbar-leading">{leading}</div> : title ? <h1>{title}</h1> : <span className="topbar-spacer" aria-hidden="true" />}
      {center && <div className="topbar-center">{center}</div>}
      {actions && <div className="topbar-actions">{actions}</div>}
    </div>
  );
}
