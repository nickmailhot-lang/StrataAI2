import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationCreationDialog } from './OrganizationCreationDialog';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';
const actor = '22222222-2222-4222-8222-222222222222';
const org = '55555555-5555-4555-8555-555555555555';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const original = { organization: { id: org, name: 'Private creation name', description: 'Private creation body', status: 0, version: 1, ownerUserId: actor, type: 'STRATA' }, role: 0 };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount() {
  const onCreated = vi.fn();
  render(<RouterProvider router={createMemoryRouter([{ path: '/', element: <OrganizationCreationDialog actorId={actor} onCreated={onCreated} onCancel={vi.fn()} /> }])} />);
  return onCreated;
}
function create() {
  fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: original.organization.name } });
  fireEvent.change(screen.getByRole('textbox', { name: 'Description' }), { target: { value: original.organization.description } });
  fireEvent.click(screen.getByRole('button', { name: /^Create$/ }));
}
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
async function observations(fetch: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1]?.body as string); const serialized = JSON.stringify(body);
  for (const secret of [actor, org, original.organization.name, original.organization.description, 'Private transport failure', 'Idempotency-Key']) expect(serialized).not.toContain(secret);
  for (const row of body.events) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
    ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return body.events as { action: string; kind: string; count: number }[];
}
it('observes original creation recovery without changing or reporting its private request', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') return new Response(null, { status: 204 });
    if (path === '/me') return reply(profile);
    if (options?.method === 'POST') { if (++writes === 1) throw new Error('Private transport failure'); return reply(original, 201); }
    return reply(original);
  }); vi.stubGlobal('fetch', fetch); const created = mount(); create();
  await screen.findByText(/may already have succeeded/);
  fireEvent.click(screen.getByRole('button', { name: 'Retry original creation' }));
  await waitFor(() => expect(created).toHaveBeenCalledWith(org));
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'organization_creation_disclosure', kind: 'open', count: 1 });
  for (const kind of ['use', 'exception', 'retry']) expect(events).toContainEqual({ action: 'organization_creation', kind, count: 1 });
  for (const kind of ['failure', 'success']) expect(events.filter(row => row.action === 'organization_creation' && row.kind === kind)).toHaveLength(1);
  const commands = fetch.mock.calls.filter(call => call[0] !== '/me/activity-client-events' && call[1]?.method === 'POST');
  expect(commands).toHaveLength(2); expect(commands[1][1]?.body).toBe(commands[0][1]?.body);
  const key = new Headers(commands[0][1]?.headers).get('Idempotency-Key');
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(key); expect(JSON.stringify(events)).not.toContain(key);
});
it('reports conflict as failure with no success and no extra mutation', async () => {
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me/activity-client-events' ? new Response(null, { status: 204 })
    : path === '/me' ? reply(profile) : reply({ code: 'conflict' }, options?.method === 'POST' ? 409 : 200));
  vi.stubGlobal('fetch', fetch); const created = mount(); create(); await screen.findByText(/Return to the directory and check/);
  const events = await observations(fetch); expect(events).toContainEqual({ action: 'organization_creation', kind: 'conflict', count: 1 });
  expect(events.filter(row => row.kind === 'failure')).toHaveLength(1); expect(events.some(row => row.kind === 'success')).toBe(false);
  expect(created).not.toHaveBeenCalled(); expect(fetch.mock.calls.filter(call => call[0] !== '/me/activity-client-events' && call[1]?.method === 'POST')).toHaveLength(1);
});
it('does not turn a recovered receipt into success before current admission', async () => {
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me/activity-client-events' ? new Response(null, { status: 204 })
    : path === '/me' ? reply(profile) : options?.method === 'POST' ? reply(original, 201) : reply({}, 403));
  vi.stubGlobal('fetch', fetch); const created = mount(); create(); await screen.findByText(/current access is unavailable/);
  const events = await observations(fetch); expect(events.filter(row => row.kind === 'failure')).toHaveLength(1);
  expect(events.some(row => row.kind === 'success')).toBe(false); expect(created).not.toHaveBeenCalled();
});
