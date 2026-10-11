import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationConfigurationPage } from './OrganizationConfigurationPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationConfigurationLive', () => ({ watchOrganizationConfiguration: live.watch }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: () => vi.fn() }));
const organizationId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const eventId = '33333333-3333-4333-8333-333333333333';
const path = `/organizations/${organizationId}/configuration`;
const profile = { id: actorId, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const revision = (version = 1) => ({ organizationId, version,
  configuration: { legalName: 'Private configuration legal name', jurisdiction: 'CA-BC', timezone: 'UTC' },
  organizationName: 'Private Organization name', organizationType: 'STRATA', organizationVersion: 1,
  actorId, eventId, correlationId: 'private-correlation',
  createdAt: '2026-10-10T13:00:00.0000001Z', updatedAt: '2026-10-10T13:00:00.0000002Z' });
function fixture(mode: 'normal' | 'lost' | 'conflict' | 'denied' | 'activityFailure' = 'normal') {
  let writes = 0; let version = 1; let activityReads = 0;
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    if (url === '/me/activity-client-events') return new Response(null, { status: 204 });
    if (url === '/me') return reply(profile);
    if (mode === 'denied') return reply({ code: 'organization_not_found' }, 404);
    if (url === path && options?.method === 'PATCH') {
      if (mode === 'conflict') return reply({ code: 'version_conflict' }, 409);
      version = 2;
      if (mode === 'lost' && ++writes === 1) throw new TypeError('Private transport diagnostic');
      return reply(revision(version));
    }
    if (url === path) return reply({ organizationId, version, revision: revision(version) });
    if (url.startsWith(`${path}/history`)) return reply({ organizationId, items: [revision(version)], nextBeforeVersion: null });
    if (url === `${path}/activity`) {
      if (mode === 'activityFailure' && ++activityReads === 1) throw new TypeError('Private transport diagnostic');
      return reply({ organizationId, items: [], nextCursor: null });
    }
    if (url === `/organizations/${organizationId}/boards/directory`) return reply({ organizationId, items: [], nextCursor: null });
    throw new Error('Unexpected telemetry fixture route');
  }); vi.stubGlobal('fetch', fetch); return fetch;
}
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/configuration', element: <OrganizationConfigurationPage /> },
    { path: '/login', element: <h1>Sign in</h1> }, { path: '/away', element: <h1>Away</h1> }],
  { initialEntries: [`/app/${organizationId}/configuration`] });
  render(<RouterProvider router={router} />); return router;
}
async function approve() {
  const review = screen.getByRole('button', { name: 'Review configuration change' });
  await waitFor(() => expect(review).toBeEnabled()); fireEvent.click(review);
  const dialog = await screen.findByRole('dialog', { name: 'Review configuration change' });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Approve configuration change' }));
}
async function observations(fetch: ReturnType<typeof fixture>) {
  await flushActivityTelemetry();
  const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events').map(call => JSON.parse(call[1]?.body as string));
  const events = reports.flatMap(report => report.events) as { action: string; kind: string; count: number; durationMs?: number }[];
  const serialized = JSON.stringify(reports);
  for (const secret of [organizationId, actorId, eventId, path, 'Private configuration legal name', 'Private Organization name',
    'private-correlation', 'Private transport diagnostic', 'Idempotency-Key']) expect(serialized).not.toContain(secret);
  for (const command of fetch.mock.calls.filter(call => call[1]?.method === 'PATCH')) {
    expect(serialized).not.toContain(new Headers(command[1]?.headers).get('Idempotency-Key'));
    expect(serialized).not.toContain(command[1]?.body);
  }
  for (const event of events) expect(Object.keys(event).sort()).toEqual(event.durationMs === undefined
    ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return events;
}
beforeEach(() => { configureActivityTelemetry(true); live.watch.mockClear(); sessionStorage.clear(); });
afterEach(() => { cleanup(); configureActivityTelemetry(false); vi.unstubAllGlobals(); });
it('records admitted opening, explicit read retry, history and retained-stream recovery with fixed fields', async () => {
  const fetch = fixture(); mount(); await screen.findByLabelText(/^Legal name/);
  fireEvent.click(screen.getByRole('button', { name: 'Load current configuration' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review configuration change' })).toBeEnabled());
  act(() => live.watch.mock.calls[0][0].reset());
  await waitFor(() => expect(screen.getByTestId('configuration-authority')).toHaveAttribute('data-ready', 'true'));
  fireEvent.click(screen.getByRole('button', { name: 'View configuration history' }));
  await screen.findByRole('heading', { name: 'Configuration history' });
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'organization_configuration_disclosure', kind: 'open', count: 1 });
  expect(events).toContainEqual({ action: 'organization_configuration_read', kind: 'retry', count: 1 });
  expect(events).toContainEqual({ action: 'organization_configuration_read', kind: 'reconnect', count: 1 });
  expect(events.some(row => row.action === 'organization_configuration_history' && row.kind === 'success')).toBe(true);
});
it('records uncertain submission and exact original retry without keys or reviewed values', async () => {
  const fetch = fixture('lost'); mount(); await screen.findByLabelText(/^Legal name/); await approve();
  await screen.findByText('The change could not be confirmed. Retry the original submission to recover its acknowledgment.');
  const retry = await screen.findByRole('button', { name: 'Retry original submission' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText('Change acknowledged. Current configuration revision 2.');
  const events = await observations(fetch);
  for (const kind of ['use', 'retry', 'exception']) expect(events).toContainEqual({ action: 'organization_configuration_change', kind, count: 1 });
  for (const kind of ['failure', 'success']) expect(events.some(row => row.action === 'organization_configuration_change' && row.kind === kind)).toBe(true);
  const commands = fetch.mock.calls.filter(call => call[1]?.method === 'PATCH'); expect(commands).toHaveLength(2);
  expect(commands[1][1]?.body).toBe(commands[0][1]?.body);
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});
it('records a definite conflict without a mutation success', async () => {
  const fetch = fixture('conflict'); mount(); await screen.findByLabelText(/^Legal name/); await approve();
  await screen.findByText('Configuration changed elsewhere. Your draft is preserved; review it against the current revision.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'organization_configuration_change', kind: 'conflict', count: 1 });
  expect(events.some(row => row.action === 'organization_configuration_change' && row.kind === 'failure')).toBe(true);
  expect(events.some(row => row.action === 'organization_configuration_change' && row.kind === 'success')).toBe(false);
});
it('does not record a disclosure before admission', async () => {
  const fetch = fixture('denied'); mount(); await screen.findByText('Organization configuration is unavailable to your account.');
  const events = await observations(fetch);
  expect(events.some(row => row.kind === 'open')).toBe(false);
  expect(events.some(row => row.action === 'organization_configuration_read' && row.kind === 'failure')).toBe(true);
});
it('records Activity failure and explicit recovery without private references or cursor data', async () => {
  const fetch = fixture('activityFailure'); mount(); await screen.findByLabelText(/^Legal name/);
  const load = screen.getByRole('button', { name: 'Load configuration Activity' });
  await waitFor(() => expect(load).toBeEnabled()); fireEvent.click(load);
  fireEvent.click(await screen.findByRole('button', { name: 'Retry Activity read' }));
  await screen.findByText('No configuration changes exist in this Activity page.');
  const events = await observations(fetch);
  for (const kind of ['use', 'retry', 'exception', 'open']) expect(events).toContainEqual({ action: 'organization_configuration_activity', kind, count: 1 });
  for (const kind of ['failure', 'success']) expect(events.some(row => row.action === 'organization_configuration_activity' && row.kind === kind)).toBe(true);
});
it('does not report a late obsolete read as admitted disclosure or success', async () => {
  const originalFetch = fixture(); let finish!: (value: Response) => void;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  const fetch = vi.fn(async (url: string, options?: RequestInit) => url === path ? pending : originalFetch(url, options));
  vi.stubGlobal('fetch', fetch); const router = mount();
  await waitFor(() => expect(fetch.mock.calls.some(call => call[0] === path)).toBe(true));
  await act(async () => { await router.navigate('/away'); });
  await act(async () => { finish(reply({ organizationId, version: 1, revision: revision() })); });
  const events = await observations(fetch);
  expect(events.some(row => row.kind === 'open' || row.kind === 'success' || row.kind === 'failure')).toBe(false);
});
