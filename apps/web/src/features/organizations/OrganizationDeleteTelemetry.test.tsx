import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationDeletePage } from './OrganizationDeletePage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

const org = '11111111-1111-4111-8111-111111111111'; const actor = '22222222-2222-4222-8222-222222222222';
const key = '33333333-3333-4333-8333-333333333333'; const eventId = '44444444-4444-4444-8444-444444444444';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const review = { organization: { id: org, name: 'Private deletion metrics Council', status: 0, version: 3 }, role: 0 };
const cache = `strataai.organization-deletion.v1:${org}`;
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
function mount() {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/delete', element: <OrganizationDeletePage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
  ], { initialEntries: [`/app/${org}/delete`] })} />);
}
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); sessionStorage.clear(); });
async function confirm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Review deletion request' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel deletion request' })).toHaveFocus());
  fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
}
async function observations(fetcher: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports.length).toBeGreaterThan(0);
  const bodies = reports.map(call => JSON.parse(call[1]?.body as string));
  for (const body of bodies) expect(Object.keys(body)).toEqual(['events']);
  const body = { events: bodies.flatMap(value => value.events) }; const text = JSON.stringify(bodies);
  const commands = fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE');
  const keys = commands.map(call => new Headers(call[1]?.headers).get('Idempotency-Key')).filter((value): value is string => !!value);
  for (const value of [org, actor, key, eventId, review.organization.name, 'Private transport failure', 'Idempotency-Key', 'expectedActorId', 'requestId', 'eventId', ...keys]) expect(text).not.toContain(value);
  for (const row of body.events as Observation[]) {
    expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
    if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
  }
  return body.events as Observation[];
}
const count = (rows: Observation[], action: string, kind: string) => rows.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);

it('observes admitted review and canceled confirmation without mutation; 202 and pending status cannot report completion', async () => {
  let requestId = ''; let statusReads = 0;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return reply(profile);
    if (init?.method === 'DELETE') { requestId = new Headers(init.headers).get('Idempotency-Key')!; return reply({ requestId }, 202); }
    if (path.includes('/deletion-requests/')) return reply(++statusReads === 1 ? { requestId, state: 'PENDING', version: 4, eventId: null, completedAt: null }
      : { requestId, state: 'COMPLETED', version: 5, eventId, completedAt: '2026-10-16T00:00:00Z' });
    return reply(review);
  }); vi.stubGlobal('fetch', fetcher); mount();
  fireEvent.click(await screen.findByRole('button', { name: 'Review deletion request' })); fireEvent.click(screen.getByRole('button', { name: 'Cancel deletion request' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: 'Review current deletion permission' })); await confirm();
  const check = await screen.findByRole('button', { name: 'Check deletion status' }); await waitFor(() => expect(check).toBeEnabled());
  expect(count(await observations(fetcher), 'organization_deletion_completion', 'open')).toBe(0);
  fireEvent.click(check); await screen.findByText('Deletion is still in progress. Completion has not been confirmed.');
  expect(count(await observations(fetcher), 'organization_deletion_completion', 'open')).toBe(0);
  await waitFor(() => expect(check).toBeEnabled()); fireEvent.click(check); await screen.findByText('Organization deletion confirmed complete.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_deletion_disclosure', 'open')).toBe(1);
  expect(count(events, 'organization_deletion_read', 'retry')).toBe(1); expect(count(events, 'organization_deletion_read', 'success')).toBe(2);
  expect(count(events, 'organization_deletion_change', 'success')).toBe(1);
  expect(count(events, 'organization_deletion_status', 'use')).toBe(1); expect(count(events, 'organization_deletion_status', 'retry')).toBe(1);
  expect(count(events, 'organization_deletion_status', 'success')).toBe(2); expect(count(events, 'organization_deletion_completion', 'open')).toBe(1);
});

it('observes lost request acknowledgment and exact original recovery without reporting terminal completion', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (init?.method === 'DELETE') { if (++writes === 1) throw new Error('Private transport failure'); return reply({ requestId: new Headers(init.headers).get('Idempotency-Key') }, 202); }
    return reply(path === '/me' ? profile : review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  const retry = await screen.findByRole('button', { name: 'Retry original deletion request' }); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText('Deletion request acknowledged. Deletion has not been confirmed complete.');
  const events = await observations(fetcher);
  for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(count(events, 'organization_deletion_change', kind)).toBe(1);
  expect(events.filter(row => row.action === 'organization_deletion_completion')).toEqual([]);
  const commands = fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(commands).toHaveLength(2); expect(commands[1][0]).toBe(commands[0][0]);
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});

it('withholds disclosure for a current non-Owner and records a failed deletion review', async () => {
  const fetcher = vi.fn(async (path: string, _init?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204) : reply(path === '/me' ? profile : { ...review, role: 1 }));
  vi.stubGlobal('fetch', fetcher); mount(); await screen.findByText('Only a current Organization Owner can request deletion.');
  const events = await observations(fetcher); expect(count(events, 'organization_deletion_disclosure', 'open')).toBe(0); expect(count(events, 'organization_deletion_read', 'failure')).toBe(1);
});

it('observes restored acknowledged reference without repeating a DELETE or inventing terminal completion', async () => {
  sessionStorage.setItem(cache, JSON.stringify({ key, actor, version: 3, acknowledged: true }));
  const fetcher = vi.fn(async (path: string, _init?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204) : reply(profile));
  vi.stubGlobal('fetch', fetcher); mount(); await screen.findByRole('button', { name: 'Check deletion status' });
  const events = await observations(fetcher); expect(count(events, 'organization_deletion_disclosure', 'open')).toBe(1); expect(count(events, 'organization_deletion_read', 'success')).toBe(1);
  expect(events.filter(row => ['organization_deletion_change', 'organization_deletion_completion'].includes(row.action))).toEqual([]);
  expect(fetcher.mock.calls.filter(call => call[0] !== '/me/activity-client-events').map(call => call[0])).toEqual(['/me']);
});

it('cannot report request success when final acknowledgment admission switches account', async () => {
  let submitted = false;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return reply(submitted ? { ...profile, id: eventId } : profile);
    if (init?.method === 'DELETE') { submitted = true; return reply({ requestId: new Headers(init.headers).get('Idempotency-Key') }, 202); }
    return reply(review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText('Sign in destination');
  const events = await observations(fetcher); expect(count(events, 'organization_deletion_change', 'success')).toBe(0); expect(count(events, 'organization_deletion_completion', 'open')).toBe(0);
});

it('cannot report completion when the account switches after the terminal status response', async () => {
  let requestId = ''; let observed = false;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return reply(observed ? { ...profile, id: eventId } : profile);
    if (init?.method === 'DELETE') { requestId = new Headers(init.headers).get('Idempotency-Key')!; return reply({ requestId }, 202); }
    if (path.includes('/deletion-requests/')) { observed = true; return reply({ requestId, state: 'COMPLETED', version: 5, eventId, completedAt: '2026-10-16T00:00:00Z' }); }
    return reply(review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  const check = await screen.findByRole('button', { name: 'Check deletion status' }); await waitFor(() => expect(check).toBeEnabled()); fireEvent.click(check); await screen.findByText('Sign in destination');
  const events = await observations(fetcher); expect(count(events, 'organization_deletion_status', 'success')).toBe(0); expect(count(events, 'organization_deletion_completion', 'open')).toBe(0);
});

it('does not repeat an acknowledged deletion when optional reporting fails', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me/activity-client-events') throw new Error('Private transport failure');
    if (init?.method === 'DELETE') { writes++; return reply({ requestId: new Headers(init.headers).get('Idempotency-Key') }, 202); }
    return reply(path === '/me' ? profile : review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByRole('button', { name: 'Check deletion status' });
  await flushActivityTelemetry(); expect(writes).toBe(1); expect(fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')).toHaveLength(1);
  expect(screen.getByText('Deletion request acknowledged. Deletion has not been confirmed complete.')).toBeVisible();
});
