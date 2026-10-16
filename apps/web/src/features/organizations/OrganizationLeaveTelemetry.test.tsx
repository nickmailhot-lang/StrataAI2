import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationLeavePage } from './OrganizationLeavePage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const summary = { organization: { id: org, name: 'Private departure Organization', status: 0 }, role: 2 };
const root = `/organizations/${org}`;
const reply = (value: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(value), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
function mount() {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/leave', element: <OrganizationLeavePage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
  ], { initialEntries: [`/app/${org}/leave`] })} />);
}
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
async function confirm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Review departure' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel departure' })).toHaveFocus());
  fireEvent.click(screen.getByRole('button', { name: 'Confirm departure' }));
}
async function observations(fetcher: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1]?.body as string); const serialized = JSON.stringify(body);
  const commands = fetcher.mock.calls.filter(call => call[0] === `${root}/leave`);
  const keys = commands.map(call => new Headers(call[1]?.headers).get('Idempotency-Key')).filter((key): key is string => !!key);
  for (const value of [org, actor, summary.organization.name, 'Private transport failure', 'expectedActorId', 'Idempotency-Key', ...keys]) expect(serialized).not.toContain(value);
  for (const row of body.events as Observation[]) {
    expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
    if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
  }
  return body.events as Observation[];
}
const count = (rows: Observation[], action: string, kind: string) => rows.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
it('observes one admitted open and explicit review retry; cancel sends no departure or change observation', async () => {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204) : reply(path === '/me' ? profile : summary));
  vi.stubGlobal('fetch', fetcher); mount();
  fireEvent.click(await screen.findByRole('button', { name: 'Review departure' }));
  fireEvent.click(screen.getByRole('button', { name: 'Cancel departure' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  fireEvent.click(screen.getByRole('button', { name: 'Review current membership' }));
  await screen.findByRole('button', { name: 'Review departure' });
  const events = await observations(fetcher);
  expect(count(events, 'organization_departure_disclosure', 'open')).toBe(1);
  expect(count(events, 'organization_departure_read', 'success')).toBe(2);
  expect(count(events, 'organization_departure_read', 'retry')).toBe(1);
  expect(events.filter(row => row.action === 'organization_departure_change')).toEqual([]);
  expect(fetcher.mock.calls.filter(call => call[0] === `${root}/leave`)).toHaveLength(0);
});
it('observes lost acknowledgment and explicit same-key/body recovery without creating another departure intent', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === `${root}/leave`) { if (++writes === 1) throw new Error('Private transport failure'); return reply(undefined, 204); }
    return reply(path === '/me' ? profile : summary);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  await screen.findByText(/Your departure could not be confirmed/);
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original departure' }));
  await screen.findByText('Original departure acknowledged. Review current membership to check later access.');
  const events = await observations(fetcher);
  for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(count(events, 'organization_departure_change', kind)).toBe(1);
  const commands = fetcher.mock.calls.filter(call => call[0] === `${root}/leave`); expect(commands).toHaveLength(2);
  expect(commands[1][1]?.body).toBe(commands[0][1]?.body);
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});
it('observes sole-owner conflict and failure without success or automatic departure retry', async () => {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204)
    : reply(path === '/me' ? profile : path === `${root}/leave` ? { code: 'sole_owner' } : summary, path === `${root}/leave` ? 409 : 200));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  await screen.findByText('The last usable owner cannot leave. Another usable owner must remain.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_departure_change', 'conflict')).toBe(1);
  expect(count(events, 'organization_departure_change', 'failure')).toBe(1);
  expect(count(events, 'organization_departure_change', 'success')).toBe(0);
  expect(fetcher.mock.calls.filter(call => call[0] === `${root}/leave`)).toHaveLength(1);
});
it('records refused review without disclosure before current membership admission', async () => {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204) : reply(path === '/me' ? profile : {}, path === '/me' ? 200 : 403));
  vi.stubGlobal('fetch', fetcher); mount(); await screen.findByText('This Organization is unavailable to your account.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_departure_disclosure', 'open')).toBe(0);
  expect(count(events, 'organization_departure_read', 'failure')).toBe(1);
});
it('does not report departure success when the account changes after the server response', async () => {
  let checks = 0;
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return reply(++checks === 4 ? { ...profile, id: '33333333-3333-4333-8333-333333333333' } : profile);
    return path === `${root}/leave` ? reply(undefined, 204) : reply(summary);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText('Sign in destination');
  const events = await observations(fetcher); expect(count(events, 'organization_departure_change', 'success')).toBe(0);
  expect(fetcher.mock.calls.filter(call => call[0] === `${root}/leave`)).toHaveLength(1);
});
it('does not create or retry departure intent when the pre-submission account check fails', async () => {
  let checks = 0;
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return ++checks === 3 ? reply({}, 503) : reply(profile);
    return reply(summary);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  await screen.findByText('Your account could not be confirmed. No departure was sent. Review current membership before trying again.');
  const events = await observations(fetcher);
  for (const kind of ['use', 'exception', 'failure']) expect(count(events, 'organization_departure_change', kind)).toBe(1);
  expect(count(events, 'organization_departure_change', 'success')).toBe(0);
  expect(fetcher.mock.calls.filter(call => call[0] === `${root}/leave`)).toHaveLength(0);
  expect(screen.queryByRole('button', { name: 'Retry original departure' })).not.toBeInTheDocument();
});
it('does not resend a confirmed departure when optional observation reporting fails', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => {
    if (path === '/me/activity-client-events') throw new Error('Private transport failure');
    if (path === `${root}/leave`) { writes++; return reply(undefined, 204); }
    return reply(path === '/me' ? profile : summary);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText('You left the Organization.');
  await flushActivityTelemetry(); expect(writes).toBe(1);
  expect(fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')).toHaveLength(1);
  expect(screen.getByText('You left the Organization.')).toBeVisible();
});
