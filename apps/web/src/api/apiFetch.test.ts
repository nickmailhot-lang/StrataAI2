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
});
