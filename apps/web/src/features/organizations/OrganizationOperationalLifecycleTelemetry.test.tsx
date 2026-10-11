import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationOperationalLifecyclePage } from './OrganizationOperationalLifecyclePage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';
import type { watchOperationalLifecycle } from './operationalLifecycleLive';
const live = vi.hoisted(() => ({ options: undefined as Parameters<typeof watchOperationalLifecycle>[0] | undefined }));
vi.mock('./operationalLifecycleLive', () => ({ watchOperationalLifecycle: (options: Parameters<typeof watchOperationalLifecycle>[0]) => {
  live.options = options; return () => {};
} }));
const actor = '22222222-2222-4222-8222-222222222222'; const org = '55555555-5555-4555-8555-555555555555';
const event = '44444444-4444-4444-8444-444444444444';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const review = { organizationId: org, actorId: actor, status: 'ACTIVE', version: 3, updatedAt: '2026-10-10T12:00:00Z', actingCapacity: 'ORGANIZATION_OWNER', name: 'Private lifecycle name' };
const route = `/organizations/${org}/operational-lifecycle`;
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function fixture(mode: 'normal' | 'lost' | 'conflict' | 'denied' | 'readback' = 'normal') {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') return new Response(null, { status: 204 });
    if (path === '/me') return response(profile);
    if (mode === 'denied') return response({}, 404);
    if (options?.method === 'PATCH') {
      writes++;
      if (mode === 'lost' && writes === 1) throw new TypeError('Private transport diagnostic');
      if (mode === 'conflict') return response({}, 409);
      const body = JSON.parse(options.body as string) as { status: string; version: number };
      return response({ organizationId: org, actorId: actor, eventId: event, actorLabel: 'Private actor caption', actingCapacity: 'ORGANIZATION_OWNER',
        previousStatus: 'ACTIVE', status: body.status, version: body.version + 1, correlationId: 'Private correlation', createdAt: '2026-10-10T12:00:01Z' });
    }
    if (mode === 'readback' && writes) throw new TypeError('Private readback diagnostic');
    return response(review);
  }); vi.stubGlobal('fetch', fetcher); return fetcher;
}
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/operational-lifecycle', element: <OrganizationOperationalLifecyclePage /> },
    { path: '/away', element: <h1>Away</h1> }], { initialEntries: [`/app/${org}/operational-lifecycle`] });
  render(<RouterProvider router={router} />); return router;
}
async function confirm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Review lifecycle change' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm lifecycle change' }));
}
async function observations(fetcher: ReturnType<typeof fixture>) {
  await flushActivityTelemetry();
  const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events').map(call => JSON.parse(call[1]?.body as string));
  const events = reports.flatMap(report => report.events) as { action: string; kind: string; count: number; durationMs?: number }[];
  const serialized = JSON.stringify(reports);
  for (const secret of [org, actor, event, route, 'Private lifecycle name', 'Private actor caption', 'Private correlation',
    'Private transport diagnostic', 'Private readback diagnostic', 'SUSPENDED', 'ACTIVE', 'ORGANIZATION_OWNER']) expect(serialized).not.toContain(secret);
  for (const call of fetcher.mock.calls.filter(call => call[1]?.method === 'PATCH')) {
    expect(serialized).not.toContain(new Headers(call[1]?.headers).get('Idempotency-Key'));
    expect(serialized).not.toContain(call[1]?.body);
  }
  for (const value of events) expect(Object.keys(value).sort()).toEqual(value.durationMs === undefined
    ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return events;
}
beforeEach(() => { configureActivityTelemetry(true); sessionStorage.clear(); live.options = undefined; });
afterEach(() => { cleanup(); configureActivityTelemetry(false); vi.unstubAllGlobals(); sessionStorage.clear(); });
it('observes admitted opening, manual retry and actual connection recovery using fixed fields', async () => {
  const fetcher = fixture(); mount(); await screen.findByRole('button', { name: 'Review lifecycle change' });
  act(() => live.options!.reconnected!());
  fireEvent.click(screen.getByRole('button', { name: 'Load current lifecycle' }));
  await screen.findByRole('button', { name: 'Review lifecycle change' });
  const events = await observations(fetcher);
  for (const kind of ['use', 'retry', 'reconnect']) expect(events).toContainEqual({ action: 'organization_lifecycle_read', kind, count: 1 });
  expect(events).toContainEqual({ action: 'organization_lifecycle_disclosure', kind: 'open', count: 1 });
  expect(events.filter(row => row.action === 'organization_lifecycle_read' && row.kind === 'success')).toHaveLength(2);
});
it('observes lost acknowledgment and explicit identical original retry without private source fields', async () => {
  const fetcher = fixture('lost'); mount(); await confirm();
  const retry = await screen.findByRole('button', { name: 'Retry original lifecycle change' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText(/Original change acknowledged/);
  const writes = fetcher.mock.calls.filter(call => call[1]?.method === 'PATCH'); expect(writes).toHaveLength(2);
  expect(writes[1][1]?.body).toBe(writes[0][1]?.body);
  expect(new Headers(writes[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(writes[0][1]?.headers).get('Idempotency-Key'));
  const events = await observations(fetcher);
  for (const kind of ['use', 'retry', 'exception']) expect(events).toContainEqual({ action: 'organization_lifecycle_change', kind, count: 1 });
  for (const kind of ['failure', 'success']) expect(events.some(row => row.action === 'organization_lifecycle_change' && row.kind === kind)).toBe(true);
});
it('observes a definite conflict without reporting mutation success', async () => {
  const fetcher = fixture('conflict'); mount(); await confirm(); await screen.findByText(/lifecycle change was refused/);
  const events = await observations(fetcher);
  expect(events).toContainEqual({ action: 'organization_lifecycle_change', kind: 'conflict', count: 1 });
  expect(events.some(row => row.action === 'organization_lifecycle_change' && row.kind === 'failure')).toBe(true);
  expect(events.some(row => row.action === 'organization_lifecycle_change' && row.kind === 'success')).toBe(false);
});
it('reports unavailable admission without counting a protected opening', async () => {
  const fetcher = fixture('denied'); mount(); await screen.findByText('Organization lifecycle is unavailable to this account.');
  const events = await observations(fetcher);
  expect(events.some(row => row.action === 'organization_lifecycle_read' && row.kind === 'failure')).toBe(true);
  expect(events.some(row => row.action === 'organization_lifecycle_disclosure')).toBe(false);
});
it('records a valid original acknowledgment independently of failed later reconciliation', async () => {
  const fetcher = fixture('readback'); mount(); await confirm(); await screen.findByText(/lifecycle change is unconfirmed/);
  const events = await observations(fetcher);
  expect(events.filter(row => row.action === 'organization_lifecycle_change' && row.kind === 'success')).toHaveLength(1);
  expect(events.some(row => row.action === 'organization_lifecycle_change' && row.kind === 'failure')).toBe(false);
});

it('ignores a protected read that completes after navigation without reporting disclosure or success', async () => {
  let resolve!: (value: Response) => void;
  const fetcher = vi.fn(async (path: string) => {
    if (path === '/me/activity-client-events') return new Response(null, { status: 204 });
    if (path === '/me') return response(profile);
    return await new Promise<Response>(done => { resolve = done; });
  }); vi.stubGlobal('fetch', fetcher);
  const router = mount(); await waitFor(() => expect(resolve).toBeDefined());
  await act(async () => { await router.navigate('/away'); });
  await act(async () => { resolve(response(review)); });
  await flushActivityTelemetry();
  const events = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')
    .flatMap(call => JSON.parse((call as unknown as [string, RequestInit])[1].body as string).events) as { action: string; kind: string }[];
  expect(events.some(row => row.kind === 'success' || row.kind === 'failure' || row.action === 'organization_lifecycle_disclosure')).toBe(false);
  expect(screen.getByRole('heading', { name: 'Away' })).toBeInTheDocument();
});
