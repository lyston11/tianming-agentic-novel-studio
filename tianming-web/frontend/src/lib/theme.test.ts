import { beforeEach, describe, expect, it } from 'vitest';
import { applyTheme, persistTheme, readCachedTheme } from './theme';

describe('theme', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('toggles the dark class only for the dark theme', () => {
    applyTheme('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);

    applyTheme('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);

    applyTheme(undefined);
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('round-trips the persisted theme and ignores empty values', () => {
    persistTheme('dark');
    expect(readCachedTheme()).toBe('dark');

    persistTheme(undefined);
    expect(readCachedTheme()).toBe('dark');

    persistTheme(null);
    expect(readCachedTheme()).toBe('dark');
  });

  it('returns null when nothing was persisted', () => {
    expect(readCachedTheme()).toBeNull();
  });
});
