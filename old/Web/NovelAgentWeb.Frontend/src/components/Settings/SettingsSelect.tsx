import { useEffect, useMemo, useRef, useState } from 'react';

export interface SettingsSelectOption {
  value: string;
  label: string;
}

interface SettingsSelectProps {
  value?: string | null;
  options: SettingsSelectOption[];
  onChange?: (value: string) => void;
  disabled?: boolean;
  placeholder?: string;
}

export default function SettingsSelect({
  value,
  options,
  onChange,
  disabled = false,
  placeholder = '请选择',
}: SettingsSelectProps) {
  const [open, setOpen] = useState(false);
  const [menuRect, setMenuRect] = useState<{ top: number; left: number; width: number } | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const normalizedValue = value ?? '';
  const selectedOption = useMemo(
    () => options.find((option) => option.value === normalizedValue),
    [normalizedValue, options],
  );

  useEffect(() => {
    if (!open) return;

    const updateMenuRect = () => {
      const rect = triggerRef.current?.getBoundingClientRect();
      if (!rect) return;
      setMenuRect({
        top: rect.bottom + 6,
        left: rect.left,
        width: rect.width,
      });
    };

    const closeOnPointerDown = (event: PointerEvent) => {
      if (rootRef.current?.contains(event.target as Node)) return;
      setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };

    updateMenuRect();
    window.addEventListener('pointerdown', closeOnPointerDown);
    window.addEventListener('keydown', closeOnEscape);
    window.addEventListener('resize', updateMenuRect);
    window.addEventListener('scroll', updateMenuRect, true);
    return () => {
      window.removeEventListener('pointerdown', closeOnPointerDown);
      window.removeEventListener('keydown', closeOnEscape);
      window.removeEventListener('resize', updateMenuRect);
      window.removeEventListener('scroll', updateMenuRect, true);
    };
  }, [open]);

  const choose = (nextValue: string) => {
    if (disabled) return;
    onChange?.(nextValue);
    setOpen(false);
  };

  const toggleOpen = () => {
    if (disabled) return;
    const rect = triggerRef.current?.getBoundingClientRect();
    if (rect) {
      setMenuRect({
        top: rect.bottom + 6,
        left: rect.left,
        width: rect.width,
      });
    }
    setOpen((current) => !current);
  };

  return (
    <div className={`settings-select${disabled ? ' disabled' : ''}`} ref={rootRef}>
      <button
        ref={triggerRef}
        className="settings-select-trigger"
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={toggleOpen}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            if (!disabled) {
              const rect = triggerRef.current?.getBoundingClientRect();
              if (rect) {
                setMenuRect({
                  top: rect.bottom + 6,
                  left: rect.left,
                  width: rect.width,
                });
              }
              setOpen(true);
            }
          }
        }}
      >
        <span>{selectedOption?.label ?? placeholder}</span>
        <i aria-hidden="true" />
      </button>

      {open && !disabled && menuRect && (
        <div
          className="settings-select-menu"
          role="listbox"
          style={{
            top: menuRect.top,
            left: menuRect.left,
            width: menuRect.width,
          }}
        >
          {options.map((option) => {
            const selected = option.value === normalizedValue;
            return (
              <button
                key={option.value}
                className={selected ? 'selected' : ''}
                type="button"
                role="option"
                aria-selected={selected}
                onClick={() => choose(option.value)}
              >
                <span>{option.label}</span>
                {selected && <em>已选</em>}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
