import { act } from '@testing-library/react';
import { checklistEvent, checklistResult, configureChecklistTelemetry, flushChecklistTelemetry } from './checklistTelemetry';

beforeEach(() => configureChecklistTelemetry(true));
afterEach(() => { configureChecklistTelemetry(false); vi.unstubAllGlobals(); vi.useRealTimers(); });
it('batches only fixed observations without identities, content, keys or exception detail', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch);
  checklistEvent('disclosure', 'open'); checklistEvent('disclosure', 'open'); checklistEvent('item_update', 'retry');
  checklistResult('item_update', true, performance.now()); await flushChecklistTelemetry();
  expect(fetch).toHaveBeenCalledTimes(1); const [path, options] = fetch.mock.calls[0]; expect(path).toBe('/me/checklist-client-events');
  expect(options.credentials).toBe('include'); expect(options.headers.get('X-StrataAI-Request')).toBe('1');
  const body = JSON.parse(options.body); expect(Object.keys(body)).toEqual(['events']);
  expect(body.events.slice(0, 2)).toEqual([{ action: 'disclosure', kind: 'open', count: 2 }, { action: 'item_update', kind: 'retry', count: 1 }]);
  expect(body.events[2]).toEqual({ action: 'item_update', kind: 'success', count: 1, durationMs: expect.any(Number) });
});
it('bounds batches and queue, and rejects invalid categories/timings before sending', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch);
  checklistEvent('private-id' as 'read', 'open'); checklistEvent('read', 'private-error' as 'open'); checklistResult('read', false, Infinity);
  for (let index = 0; index < 200; index++) checklistResult('item_update', true, performance.now());
  for (let index = 0; index < 7; index++) await flushChecklistTelemetry();
  expect(fetch).toHaveBeenCalledTimes(6); expect(fetch.mock.calls.every(call => JSON.parse(call[1].body).events.length === 20)).toBe(true);
  expect(JSON.stringify(fetch.mock.calls.map(call => call[1].body))).not.toContain('private');
});
it('discards failed reports without retries and aborts suspended transport', async () => {
  vi.useFakeTimers(); const fetch = vi.fn().mockRejectedValueOnce(new Error('private transport detail'))
    .mockImplementationOnce((_path: string, options: RequestInit) => new Promise<Response>((_resolve, reject) => options.signal?.addEventListener('abort', () => reject(new Error('aborted')))));
  vi.stubGlobal('fetch', fetch); checklistEvent('create', 'exception'); await flushChecklistTelemetry();
  await act(() => vi.advanceTimersByTimeAsync(10000)); expect(fetch).toHaveBeenCalledTimes(1);
  checklistEvent('create', 'use'); const stalled = flushChecklistTelemetry();
  await act(() => vi.advanceTimersByTimeAsync(3000)); await stalled; expect(fetch).toHaveBeenCalledTimes(2);
});
it('does no network work when disabled', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch); configureChecklistTelemetry(false);
  checklistEvent('disclosure', 'open'); checklistResult('create', true, performance.now()); await flushChecklistTelemetry(); expect(fetch).not.toHaveBeenCalled();
});
