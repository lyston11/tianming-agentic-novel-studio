import { useState, type ReactNode } from 'react';
import '../../styles/collapse-panel.css';

interface CollapsePanelProps {
  id: string;
  title: string;
  children: ReactNode;
  defaultOpen?: boolean;
  onToggle?: (isOpen: boolean) => void;
}

function storageKey(id: string) {
  return `settings-collapse-${id}`;
}

function readInitialOpenState(id: string, defaultOpen: boolean) {
  if (typeof window === 'undefined') return defaultOpen;

  try {
    const stored = window.localStorage.getItem(storageKey(id));
    if (stored === 'true') return true;
    if (stored === 'false') return false;
  } catch {
    return defaultOpen;
  }

  return defaultOpen;
}

function persistOpenState(id: string, isOpen: boolean) {
  if (typeof window === 'undefined') return;

  try {
    window.localStorage.setItem(storageKey(id), String(isOpen));
  } catch {
    // Ignore storage failures so the panel remains usable in restricted browser modes.
  }
}

export default function CollapsePanel({ id, title, children, defaultOpen = false, onToggle }: CollapsePanelProps) {
  const [isOpen, setIsOpen] = useState(() => readInitialOpenState(id, defaultOpen));

  const toggle = () => {
    const newState = !isOpen;
    setIsOpen(newState);
    persistOpenState(id, newState);
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
