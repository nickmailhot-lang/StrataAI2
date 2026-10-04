import { normalizeApiProblem } from './apiProblem';

afterEach(() => vi.useRealTimers());
describe('ARCH-02 common Problem normalization', () => {
  it.each(['mention_handle_invalid', 'mention_handle_unavailable', 'mention_handle_claim_refused', 'invalid_idempotency_key'])(
    'retains approved account handle refusal %s while excluding private response fields', async code => {
      const response = await normalizeApiProblem(new Response(JSON.stringify({ code, handle: 'private', userId: 'private', detail: 'private' }), { status: 409 }));
      expect(await response.json()).toEqual({ type: 'about:blank', title: 'Unable to complete this request.', status: 409, code });
    });
  it('retains actual status, approved code and recovery headers, excluding server details', async () => {
    const response = await normalizeApiProblem(new Response(JSON.stringify({ status: 200, code: 'idempotency_key_expired',
      title: 'private SQL', detail: 'private recipient', stack: 'private stack', user: { id: 'private ID' } }), {
      status: 409, headers: { 'X-Correlation-ID': 'test-correlation', 'Retry-After': '60', 'Content-Length': '999' },
    }));
    expect(response.status).toBe(409); expect(response.ok).toBe(false);
    expect(await response.json()).toEqual({ type: 'about:blank', title: 'Unable to complete this request.', status: 409, code: 'idempotency_key_expired' });
    expect(response.headers.get('X-Correlation-ID')).toBe('test-correlation');
    expect(response.headers.get('Retry-After')).toBe('60'); expect(response.headers.has('Content-Length')).toBe(false);
  });
  it.each(['<html>private proxy error</html>', JSON.stringify({ code: 'private_recipient_data', detail: 'private' }), 'x'.repeat(16_385)])(
    'normalizes unknown, malformed and oversized error bodies without exposing them', async body => {
      const response = await normalizeApiProblem(new Response(body, { status: 503 }));
      expect(await response.json()).toEqual({ type: 'about:blank', title: 'Unable to complete this request.', status: 503 });
    });
  it('keeps successful acknowledgments untouched', async () => {
    const response = new Response(JSON.stringify({ id: 'acknowledgment', version: 2 }), { status: 200 });
    expect(await normalizeApiProblem(response)).toBe(response);
    expect(await response.json()).toEqual({ id: 'acknowledgment', version: 2 });
  });
  it('bounds an unresponsive error body and cancels its reader', async () => {
    vi.useFakeTimers(); const cancel = vi.fn();
    const response = normalizeApiProblem(new Response(new ReadableStream({ cancel }), { status: 502 }));
    await vi.advanceTimersByTimeAsync(5000);
    expect(await (await response).json()).toMatchObject({ status: 502 }); expect(cancel).toHaveBeenCalledOnce();
  });
});
