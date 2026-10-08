import { installRuntimeExceptionObservers } from './runtimeExceptions';
import { configureActivityTelemetry, flushActivityTelemetry } from '../features/kanban/activityTelemetry';

let dispose: (() => void) | undefined;
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { dispose?.(); dispose = undefined; configureActivityTelemetry(false); vi.unstubAllGlobals(); });

it('counts exceptions without accessing private event fields or altering other listeners', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetcher);
  dispose = installRuntimeExceptionObservers(true);
  const error = new ErrorEvent('error', { cancelable: true });
  for (const field of ['error', 'message', 'filename', 'lineno', 'colno'])
    Object.defineProperty(error, field, { get: () => { throw new Error('private field accessed'); } });
  const rejection = new Event('unhandledrejection', { cancelable: true });
  Object.defineProperty(rejection, 'reason', { get: () => { throw new Error('private reason accessed'); } });
  const anotherListener = vi.fn(); window.addEventListener('error', anotherListener);
  try {
    expect(window.dispatchEvent(error)).toBe(false);
    expect(window.dispatchEvent(rejection)).toBe(false);
    expect(anotherListener).toHaveBeenCalledOnce();
    await flushActivityTelemetry();
    expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ events: [
      { action: 'application_event_exception', kind: 'exception', count: 1 },
      { action: 'application_promise_exception', kind: 'exception', count: 1 },
    ] });
  } finally { window.removeEventListener('error', anotherListener); }
});

it('ignores resource errors and detaches all observers on disposal', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  dispose = installRuntimeExceptionObservers(true);
  expect(window.dispatchEvent(new Event('error', { cancelable: true }))).toBe(true);
  dispose(); dispose();
  expect(window.dispatchEvent(new ErrorEvent('error', { cancelable: true }))).toBe(true);
  expect(window.dispatchEvent(new Event('unhandledrejection', { cancelable: true }))).toBe(true);
  await flushActivityTelemetry(); expect(fetcher).not.toHaveBeenCalled();
});

it('keeps browser default diagnostics in development', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  dispose = installRuntimeExceptionObservers(false);
  expect(window.dispatchEvent(new ErrorEvent('error', { cancelable: true }))).toBe(true);
  expect(window.dispatchEvent(new Event('unhandledrejection', { cancelable: true }))).toBe(true);
  await flushActivityTelemetry(); expect(fetcher).not.toHaveBeenCalled();
});
