const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';

function getToken(): string | null {
  try {
    const stored = localStorage.getItem('auth-storage');
    if (!stored) return null;
    const parsed = JSON.parse(stored);
    return parsed?.state?.token || null;
  } catch {
    return null;
  }
}

export async function api<T>(path: string, options?: RequestInit): Promise<T> {
  // Get token from localStorage (Zustand persist format)
  const token = getToken();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };

  // Add Authorization header if token exists
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  // Merge with any additional headers from options
  if (options?.headers) {
    Object.entries(options.headers).forEach(([key, value]) => {
      if (typeof value === 'string') {
        headers[key] = value;
      }
    });
  }

  const response = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers,
  });
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(text || `${response.status} ${response.statusText}`);
  }
  return response.json();
}

export const get = <T>(path: string) => api<T>(path);

export const post = <T>(path: string, body?: unknown) =>
  api<T>(path, {
    method: 'POST',
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
