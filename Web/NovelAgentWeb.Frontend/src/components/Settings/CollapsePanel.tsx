import { useState, useEffect, type ReactNode } from 'react';
import '../../styles/collapse-panel.css';

interface CollapsePanelProps {
  id: string;
  title: string;
  children: ReactNode;
  defaultOpen?: boolean;
  onToggle?: (isOpen: boolean) => void;
}

export default function CollapsePanel({ id, title, children, defaultOpen = false, onToggle }: CollapsePanelProps) {
  const [isOpen, setIsOpen] = useState(defaultOpen);

  useEffect(() => {
    const stored = localStorage.getItem(`settings-collapse-${id}`);
    if (stored !== null) {
      setIsOpen(stored === 'true');
    }
  }, [id]);

  const toggle = () => {
    const newState = !isOpen;
    setIsOpen(newState);
    localStorage.setItem(`settings-collapse-${id}`, String(newState));
    onToggle?.(newState);
  };

  return (
    <div className="collapse-panel">
      <button
        className="collapse-header"
        onClick={toggle}
        type="button"
        aria-expanded={isOpen}
      >
        <span className="collapse-title">{title}</span>
        <span className={`collapse-icon ${isOpen ? 'open' : ''}`}>▼</span>
      </button>
      <div className={`collapse-content ${isOpen ? 'open' : ''}`}>
        <div className="collapse-body">
          {children}
        </div>
      </div>
    </div>
  );
}
