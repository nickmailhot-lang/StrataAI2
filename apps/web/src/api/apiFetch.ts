/** Same-origin API transport; a custom header protects mutations from CSRF. */
export function apiFetch(path: string, options: RequestInit = {}) {
  if (!path.startsWith('/') || path.startsWith('//')) {
    throw new Error('API requests must use a same-origin path.');
  }
  const method = (options.method ?? 'GET').toUpperCase();
  const headers = new Headers(options.headers);
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    headers.set('X-StrataAI-Request', '1');
  }
  return fetch(path, { credentials: 'include', ...options, headers });
}
