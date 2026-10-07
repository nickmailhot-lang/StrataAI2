import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationSettingsPage } from './OrganizationSettingsPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const org = { id: '11111111-1111-4111-8111-111111111111', name: 'Private council', description: 'Private draft body', logoUrl: 'https://private.example/logo', status: 0, version: 1 };
const profile = { id: '22222222-2222-4222-8222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const summary = { organization: org, role: 0 };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount() {
  render(<RouterProvider router={createMemoryRouter([{ path: '/app/:organizationId/settings', element: <OrganizationSettingsPage /> }],
    { initialEntries: [`/app/${org.id}/settings`] })} />);
}
beforeEach(() => { configureActivityTelemetry(true); live.watch.mockClear(); });
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
async function observations(fetch: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1]?.body as string); const serialized = JSON.stringify(body);
  for (const secret of [org.id, org.name, org.description, org.logoUrl, profile.id, 'Private transport failure', 'Idempotency-Key'])
    expect(serialized).not.toContain(secret);
  for (const row of body.events) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
    ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return body.events as { action: string; kind: string; count: number }[];
}
it('reports one admitted open, explicit read retry and live recovery without protected settings', async () => {
  const fetch = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? new Response(null, { status: 204 })
    : reply(path === '/me' ? profile : summary)); vi.stubGlobal('fetch', fetch);
  mount(); await screen.findByLabelText(/^Organization name/);
  fireEvent.click(screen.getByRole('button', { name: 'Load current settings' }));
  await screen.findByText('Current Organization settings match your draft.');
  act(() => live.watch.mock.calls[0][0].reset());
  await screen.findByText('Current settings checked. Review any saved changes before replacing them with your draft.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'organization_settings_disclosure', kind: 'open', count: 1 });
  expect(events).toContainEqual({ action: 'organization_settings_read', kind: 'retry', count: 2 });
  expect(events.filter(row => row.action === 'organization_settings_read' && row.kind === 'success')).toHaveLength(3);
});
it('reports uncertainty and original save retry while preserving exact mutation identity', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') return new Response(null, { status: 204 });
    if (path === '/me') return reply(profile);
    if (options?.method === 'PATCH') { if (++writes === 1) throw new Error('Private transport failure'); return reply({ ...org, version: 2 }); }
    return reply(summary);
  }); vi.stubGlobal('fetch', fetch);
  mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(screen.getByRole('button', { name: 'Save Organization settings' }));
  await screen.findByText(/Your save could not be confirmed/);
  fireEvent.click(screen.getByRole('button', { name: 'Retry original save' })); await screen.findByText(/Original save acknowledgment recovered/);
  const events = await observations(fetch);
  for (const kind of ['use', 'exception', 'retry']) expect(events).toContainEqual({ action: 'organization_settings_update', kind, count: 1 });
  expect(events.some(row => row.action === 'organization_settings_update' && row.kind === 'failure')).toBe(true);
  expect(events.some(row => row.action === 'organization_settings_update' && row.kind === 'success')).toBe(true);
  const commands = fetch.mock.calls.filter(call => call[1]?.method === 'PATCH'); expect(commands).toHaveLength(2);
  expect(commands[1][1]?.body).toBe(commands[0][1]?.body);
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});
it('reports a refused version conflict without a success observation', async () => {
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me/activity-client-events' ? new Response(null, { status: 204 })
    : reply(path === '/me' ? profile : options?.method === 'PATCH' ? { code: 'version_conflict' } : summary, options?.method === 'PATCH' ? 409 : 200));
  vi.stubGlobal('fetch', fetch); mount(); await screen.findByLabelText(/^Organization name/);
  fireEvent.click(screen.getByRole('button', { name: 'Save Organization settings' })); await screen.findByText(/Organization changed elsewhere/);
  const events = await observations(fetch); expect(events).toContainEqual({ action: 'organization_settings_update', kind: 'conflict', count: 1 });
  expect(events.some(row => row.action === 'organization_settings_update' && row.kind === 'failure')).toBe(true);
  expect(events.some(row => row.action === 'organization_settings_update' && row.kind === 'success')).toBe(false);
});
it('does not count an open before administrative admission', async () => {
  const fetch = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? new Response(null, { status: 204 })
    : path === '/me' ? reply(profile) : reply({}, 403)); vi.stubGlobal('fetch', fetch); mount();
  await screen.findByText('Organization settings are unavailable to your account.');
  await waitFor(() => expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument());
  const events = await observations(fetch); expect(events.some(row => row.kind === 'open')).toBe(false);
  expect(events.some(row => row.action === 'organization_settings_read' && row.kind === 'failure')).toBe(true);
});
