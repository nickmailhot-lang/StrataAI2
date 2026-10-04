import { act } from '@testing-library/react';
import { activityEvent, activityResult, configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';

beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.useRealTimers(); });
it('batches only fixed observations without identities, content, keys or exception detail', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch);
  activityEvent('card_disclosure', 'open'); activityEvent('card_disclosure', 'open'); activityEvent('card_read', 'retry');
  activityResult('card_read', true, performance.now()); await flushActivityTelemetry();
  expect(fetch).toHaveBeenCalledTimes(1); const [path, options] = fetch.mock.calls[0]; expect(path).toBe('/me/activity-client-events');
  expect(options.credentials).toBe('include'); expect(options.headers.get('X-StrataAI-Request')).toBe('1');
  const body = JSON.parse(options.body); expect(Object.keys(body)).toEqual(['events']);
  expect(body.events.slice(0, 2)).toEqual([{ action: 'card_disclosure', kind: 'open', count: 2 }, { action: 'card_read', kind: 'retry', count: 1 }]);
  expect(body.events[2]).toEqual({ action: 'card_read', kind: 'success', count: 1, durationMs: expect.any(Number) });
});
it('bounds batches and queue, and rejects invalid categories/timings before sending', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch);
  activityEvent('private-id' as 'card_read', 'open'); activityEvent('card_read', 'private-error' as 'open'); activityResult('card_read', false, Infinity);
  for (let index = 0; index < 200; index++) activityResult('card_read', true, performance.now());
  for (let index = 0; index < 7; index++) await flushActivityTelemetry();
  expect(fetch).toHaveBeenCalledTimes(6); expect(fetch.mock.calls.every(call => JSON.parse(call[1].body).events.length === 20)).toBe(true);
  expect(JSON.stringify(fetch.mock.calls.map(call => call[1].body))).not.toContain('private');
});
it('discards failed reports without retries and aborts suspended transport', async () => {
  vi.useFakeTimers(); const fetch = vi.fn().mockRejectedValueOnce(new Error('private transport detail'))
    .mockImplementationOnce((_path: string, options: RequestInit) => new Promise<Response>((_resolve, reject) => options.signal?.addEventListener('abort', () => reject(new Error('aborted')))));
  vi.stubGlobal('fetch', fetch); activityEvent('board_read', 'exception'); await flushActivityTelemetry();
  await act(() => vi.advanceTimersByTimeAsync(10000)); expect(fetch).toHaveBeenCalledTimes(1);
  activityEvent('board_read', 'use'); const stalled = flushActivityTelemetry();
  await act(() => vi.advanceTimersByTimeAsync(3000)); await stalled; expect(fetch).toHaveBeenCalledTimes(2);
});
it('does no network work when disabled', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch); configureActivityTelemetry(false);
  activityEvent('card_disclosure', 'open'); activityResult('board_read', true, performance.now()); await flushActivityTelemetry(); expect(fetch).not.toHaveBeenCalled();
});
