export const THEME_STORAGE_KEY = 'theme-storage';

/**
 * Read the persisted theme ('light' | 'dark') saved by ThemeSync, or null when
 * nothing was saved yet or storage is unavailable.
 */
export function readCachedTheme(): string | null {
  if (typeof localStorage === 'undefined') return null;
  try {
    return localStorage.getItem(THEME_STORAGE_KEY);
  } catch {
    return null;
  }
}

/**
 * Toggle the document-level `dark` class that the index.css token blocks
 * (`:root` light / `.dark`) key off. Any value other than 'dark' falls back to
 * the light default.
 */
export function applyTheme(theme: string | null | undefined): void {
  document.documentElement.classList.toggle('dark', theme === 'dark');
}

/** Persist a theme value so the next startup can apply it before React renders. */
export function persistTheme(theme: string | null | undefined): void {
  if (!theme || typeof localStorage === 'undefined') return;
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
    // Storage can throw (private mode / quota); the theme then stays session-local.
  }
}
