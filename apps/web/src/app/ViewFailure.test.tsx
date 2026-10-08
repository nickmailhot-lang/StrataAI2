import { StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { act, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ViewFailure, observeViewFailure, viewFailureRootOptions } from './ViewFailure';
import { configureActivityTelemetry, flushActivityTelemetry } from '../features/kanban/activityTelemetry';

let root: Root | undefined;
let host: HTMLDivElement | undefined;
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => {
  if (root) act(() => root!.unmount()); root = undefined; host?.remove(); host = undefined;
  configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.restoreAllMocks();
});

it.each([
  ['/app/private-org/boards/private-board', 'board_render'],
  ['/app/private-org/boards/private-board/cards/private-card', 'card_render'],
  ['/app/profile', 'application_render'],
])('reports a real caught render failure on %s without retaining private diagnostics', async (path, action) => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetcher);
  const diagnostic = vi.spyOn(console, 'error').mockImplementation(() => {});
  function BrokenView(): never { throw new Error('private-render-body-and-stack'); }
  const router = createMemoryRouter([{ path: '*', element: <BrokenView />, errorElement: <ViewFailure /> }], { initialEntries: [path] });
  host = document.createElement('div'); document.body.append(host);
  root = createRoot(host, viewFailureRootOptions(true));
  await act(async () => root!.render(<StrictMode><RouterProvider router={router} onError={observeViewFailure} /></StrictMode>));
  expect(screen.getByRole('alert')).toHaveTextContent('This view is unavailable.');
  expect(screen.getByText('Reloading may discard unsaved changes.')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Reload this page' })).toBeEnabled();
  expect(document.body.textContent).not.toContain('private');
  expect(diagnostic).toHaveBeenCalledWith('A view could not be rendered.');
  expect(diagnostic.mock.calls.flat().every(value => value === 'A view could not be rendered.')).toBe(true);
  expect(fetcher).not.toHaveBeenCalled(); // No automatic retry/reload or mutation.
  await flushActivityTelemetry();
  expect(fetcher).toHaveBeenCalledTimes(1);
  expect(fetcher.mock.calls[0][0]).toBe('/me/activity-client-events');
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ events: [{ action, kind: 'exception', count: 1 }] });
});

it('leaves development root diagnostics unchanged and ignores non-render router failures', async () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  expect(viewFailureRootOptions(false)).toEqual({});
  observeViewFailure(new Error('private loader error'), {
    location: { pathname: '/app/private-org/boards/private-board', search: '?private=value', hash: '', state: null, key: 'private-key' },
    params: { organizationId: 'private-org' }, pattern: '/app/:organizationId/boards/:boardId',
  });
  await flushActivityTelemetry(); expect(fetcher).not.toHaveBeenCalled();
});

it('observes a real uncaught root failure without printing its Error or component stack', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetcher);
  const diagnostic = vi.spyOn(console, 'error').mockImplementation(() => {});
  function BrokenRoot(): never { throw new Error('private-root-material'); }
  host = document.createElement('div'); document.body.append(host);
  root = createRoot(host, viewFailureRootOptions(true));
  // React's act queue deliberately rethrows uncaught errors instead of calling
  // onUncaughtError. Exercise the actual root callback outside that test queue.
  const testEnvironment = globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean };
  const actEnvironment = testEnvironment.IS_REACT_ACT_ENVIRONMENT;
  testEnvironment.IS_REACT_ACT_ENVIRONMENT = false;
  try { flushSync(() => root!.render(<BrokenRoot />)); }
  finally { testEnvironment.IS_REACT_ACT_ENVIRONMENT = actEnvironment; }
  await flushActivityTelemetry();
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ events: [{ action: 'application_root_exception', kind: 'exception', count: 1 }] });
  expect(diagnostic.mock.calls.flat()).toEqual(['A view could not be rendered.']);
});

it('reports recoverable root errors using a fixed count without retaining their details', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetcher);
  const diagnostic = vi.spyOn(console, 'error').mockImplementation(() => {});
  const options = viewFailureRootOptions(true);
  expect(options.onRecoverableError).toBeTypeOf('function');
  options.onRecoverableError?.(new Error('private-recovery-material'), { componentStack: 'private-component-stack' });
  await flushActivityTelemetry();
  expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ events: [{ action: 'application_recovery_exception', kind: 'exception', count: 1 }] });
  expect(diagnostic.mock.calls.flat()).toEqual(['A view could not be rendered.']);
});
