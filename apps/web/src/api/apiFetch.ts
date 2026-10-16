import { normalizeApiProblem } from './apiProblem';

function isSameOriginPath(path: string) {
  if (!path.startsWith('/') || path.startsWith('//')) return false;
  try {
    // URL parsing can turn slash/backslash or embedded newlines into an
    // authority. Check the browser's interpretation before sending anything.
    return new URL(path, window.location.href).origin === window.location.origin;
  } catch { return false; }
}

/** Same-origin transport, CSRF protection and one normalized Problem boundary. */
export function apiFetch(path: string, options: RequestInit = {}) {
  if (!isSameOriginPath(path)) {
    throw new Error('API requests must use a same-origin path.');
  }
  const method = (options.method ?? 'GET').toUpperCase();
  const headers = new Headers(options.headers);
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    headers.set('X-StrataAI-Request', '1');
  }
  // Enforce this after caller options: it also fences redirects and a changed
  // document base URL without relying on CSP or a foreign server's CORS policy.
  return fetch(path, { credentials: 'include', ...options, headers, mode: 'same-origin' }).then(normalizeApiProblem);
}
