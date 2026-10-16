import { apiFetch } from './apiFetch';

afterEach(() => vi.unstubAllGlobals());

describe('PRD-24 CSRF transport', () => {
  it.each(['POST', 'PATCH', 'PUT', 'DELETE'])('adds the protection header to %s', async method => {
    const fetchMock = vi.fn().mockResolvedValue(new Response());
    vi.stubGlobal('fetch', fetchMock);
    await apiFetch('/me', { method, headers: { 'Content-Type': 'application/json' } });
    expect(fetchMock.mock.calls[0][1].headers.get('X-StrataAI-Request')).toBe('1');
    expect(fetchMock.mock.calls[0][1].headers.get('Content-Type')).toBe('application/json');
    expect(fetchMock.mock.calls[0][1].credentials).toBe('include');
  });
  it('rejects cross-origin and protocol-relative targets before sending', () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    expect(() => apiFetch('https://attacker.example/me', { method: 'POST' })).toThrow();
    expect(() => apiFetch('//attacker.example/me', { method: 'POST' })).toThrow();
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it.each(['/\\attacker.example/me', '/\t/attacker.example/me', '/\r/attacker.example/me', '/\n/attacker.example/me'])(
    'ARCH-02/PRD-24: refuses a path that the browser resolves to another origin: %j', path => {
      expect(new URL(path, window.location.href).origin).not.toBe(window.location.origin);
      const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock);
      expect(() => apiFetch(path, { method: 'POST', body: 'synthetic-private-body' })).toThrow('API requests must use a same-origin path.');
      expect(fetchMock).not.toHaveBeenCalled();
    });
  it.each([undefined, 'cors', 'no-cors'] as const)(
    'ARCH-02/PRD-24: enforces browser same-origin mode despite caller mode %s', async mode => {
      const response = new Response(); const fetchMock = vi.fn().mockResolvedValue(response); vi.stubGlobal('fetch', fetchMock);
      const controller = new AbortController();
      expect(await apiFetch('/me?after=123', { method: 'PATCH', mode, signal: controller.signal,
        headers: { 'X-StrataAI-Expected-Actor': 'synthetic-actor' }, body: 'synthetic-body' })).toBe(response);
      const [path, options] = fetchMock.mock.calls[0];
      expect(path).toBe('/me?after=123'); expect(options.mode).toBe('same-origin');
      expect(options.credentials).toBe('include'); expect(options.body).toBe('synthetic-body');
      expect(options.signal).toBe(controller.signal);
      expect(options.headers.get('X-StrataAI-Expected-Actor')).toBe('synthetic-actor');
      expect(options.headers.get('X-StrataAI-Request')).toBe('1');
    });
});
