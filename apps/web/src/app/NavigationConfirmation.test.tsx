import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { NavigationConfirmation } from './NavigationConfirmation';
import { configureActivityTelemetry, flushActivityTelemetry } from '../features/kanban/activityTelemetry';
const actor = '11111111-1111-4111-8111-111111111111';
const organization = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const event = '44444444-4444-4444-8444-444444444444';
const target = { kind: 'board' as const, organization, board, version: 3 };
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
const source = { eventId: event, actorId: actor, organizationId: organization, boardId: board,
  eventType: 'BOARD_OPENED', entityType: 'Board', entityId: board, version: 3, metadata: {}, createdAt: '2026-10-05T12:00:00Z' };
afterEach(() => { cleanup(); configureActivityTelemetry(false); vi.unstubAllGlobals(); });
beforeEach(() => sessionStorage.clear());

it('PRD-01 records content-free confirmation results and explicit retries independently of the original event', async () => {
  configureActivityTelemetry(true);
  let writes = 0; let report: { events: { action: string; kind: string; count: number; durationMs?: number }[] } | undefined;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response({ id: actor });
    if (path === '/me/activity-client-events') { report = JSON.parse(options!.body as string); throw new Error('Telemetry unavailable'); }
    if (++writes === 1) throw new TypeError('Private network detail');
    return response(source);
  }); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={target} />);
  fireEvent.click(await screen.findByRole('button', { name: 'Retry navigation confirmation' }));
  await waitFor(() => expect(sessionStorage.length).toBe(0));
  await flushActivityTelemetry();
  expect(writes).toBe(2);
  expect(report!.events).toEqual(expect.arrayContaining([
    { action: 'navigation_board', kind: 'open', count: 1 },
    { action: 'navigation_board', kind: 'use', count: 2 },
    { action: 'navigation_board', kind: 'retry', count: 1 },
    { action: 'navigation_board', kind: 'exception', count: 1 },
    { action: 'navigation_board', kind: 'failure', count: 1, durationMs: expect.any(Number) },
    { action: 'navigation_board', kind: 'success', count: 1, durationMs: expect.any(Number) },
  ]));
  for (const entry of report!.events) expect(Object.keys(entry).every(key => ['action', 'kind', 'count', 'durationMs'].includes(key))).toBe(true);
  for (const privateValue of [actor, organization, board, event, 'Private network detail', '/navigation/observations'])
    expect(JSON.stringify(report)).not.toContain(privateValue);
  expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull();
});

it('PRD-01 invalid navigation identities cannot dispatch account or observation reads', () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={{ ...target, board: 'board-placeholder' }} />);
  expect(fetch).not.toHaveBeenCalled();
  expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull();
});
it('PRD-01 confirms Organization context without Board or Card scope', async () => {
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? { id: actor } : {
    ...source, eventType: 'APPLICATION_CONTEXT_CHANGED', entityType: 'Organization', entityId: organization, boardId: null, version: 1,
  })); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={{ kind: 'context', organization }} />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(4));
  expect(fetch.mock.calls.filter(([path]) => path !== '/me')[0][0])
    .toBe(`/navigation/observations?kind=context&organizationId=${organization}`);
  expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull();
});
it('PRD-01 confirms global context without retaining an Organization identifier', async () => {
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? { id: actor } : {
    ...source, eventType: 'APPLICATION_CONTEXT_CHANGED', entityType: 'ApplicationContext', entityId: event,
    organizationId: null, boardId: null, version: 1,
  })); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={{ kind: 'context', organization: null }} />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(4));
  expect(fetch.mock.calls.filter(([path]) => path !== '/me')[0][0]).toBe('/navigation/observations?kind=context');
  await waitFor(() => expect(sessionStorage.length).toBe(0));
});

it('PRD-01 waits for admission and does not create another open for an entity edit', async () => {
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? { id: actor } : source));
  vi.stubGlobal('fetch', fetch);
  const view = render(<NavigationConfirmation target={target} admitted={false} />);
  expect(fetch).not.toHaveBeenCalled();
  view.rerender(<NavigationConfirmation target={target} admitted />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(4));
  view.rerender(<NavigationConfirmation target={{ ...target, version: 4 }} admitted />);
  expect(fetch.mock.calls.filter(([path]) => path !== '/me')).toHaveLength(1);
});
it('PRD-01 offers explicit recovery using the original key and revision after a lost reply', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: actor });
    if (++writes === 1) throw new TypeError('Lost response');
    return response(source);
  }); vi.stubGlobal('fetch', fetch);
  const view = render(<NavigationConfirmation target={target} />);
  const retry = await screen.findByRole('button', { name: 'Retry navigation confirmation' });
  view.rerender(<NavigationConfirmation target={{ ...target, version: 4 }} />);
  fireEvent.click(retry);
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull());
  await waitFor(() => expect(writes).toBe(2));
  const requests = fetch.mock.calls.filter(([path]) => path !== '/me') as unknown as [string, RequestInit][];
  expect(requests[0][0]).toBe(requests[1][0]);
  expect(new Headers(requests[0][1].headers).get('Idempotency-Key')).toBe(new Headers(requests[1][1].headers).get('Idempotency-Key'));
});
it('PRD-01 anonymous visitors create no personal navigation event', async () => {
  const fetch = vi.fn(async () => new Response('{}', { status: 401 })); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={target} />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  expect(fetch.mock.calls).toEqual([['/me', expect.anything()]]);
  expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull();
});
it('PRD-01 recovers a lost confirmation after returning to the same Board with a newer snapshot', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: actor });
    if (++writes === 1) throw new TypeError('Lost response');
    return response(source);
  }); vi.stubGlobal('fetch', fetch);
  const first = render(<NavigationConfirmation target={target} />);
  await screen.findByRole('button', { name: 'Retry navigation confirmation' });
  expect(sessionStorage.length).toBe(1); first.unmount();
  render(<NavigationConfirmation target={{ ...target, version: 4 }} />);
  await waitFor(() => expect(sessionStorage.length).toBe(0));
  const requests = fetch.mock.calls.filter(([path]) => path !== '/me') as unknown as [string, RequestInit][];
  expect(requests).toHaveLength(2); expect(requests[0][0]).toBe(requests[1][0]);
  expect(new Headers(requests[0][1].headers).get('Idempotency-Key')).toBe(new Headers(requests[1][1].headers).get('Idempotency-Key'));
});
