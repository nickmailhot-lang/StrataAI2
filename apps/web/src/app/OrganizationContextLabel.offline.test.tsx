import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AppThemeFixture } from '../test/AppThemeFixture';
import { OrganizationContextLabel } from './OrganizationContextLabel';

type MetadataOptions = Parameters<typeof import('../features/organizations/organizationMetadataLive').watchOrganizationMetadata>[0];
const metadata = vi.hoisted(() => ({ stop: vi.fn(), watch: vi.fn<(options: MetadataOptions) => () => void>() }));
vi.mock('../features/organizations/organizationMetadataLive', () => ({ watchOrganizationMetadata: metadata.watch }));
const organization = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const summary = (name = 'Current council') => ({ organization: { id: organization, name, status: 0 }, role: 2 });
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
let online = true;
beforeEach(() => {
  online = true;
  vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online);
  metadata.stop.mockReset(); metadata.watch.mockReset(); metadata.watch.mockImplementation(() => metadata.stop);
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });
function mount() {
  render(<AppThemeFixture><MemoryRouter initialEntries={[`/app/${organization}`]}><Routes>
    <Route path="/app/:organizationId" element={<OrganizationContextLabel />} />
  </Routes></MemoryRouter></AppThemeFixture>);
}
function disconnect() { online = false; act(() => { fireEvent(window, new Event('offline')); }); }
function reconnect() { online = true; act(() => { fireEvent(window, new Event('online')); }); }

it('PRD-03-VS-01-T-02: immediately withdraws the header name and its subscription when the browser goes offline', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => reply(path === '/me' ? profile : summary())));
  mount(); await screen.findByText('Current council');
  disconnect();
  expect(screen.queryByText('Current council')).toBeNull();
  expect(screen.getByText('Organization', { exact: true })).toBeVisible();
  expect(metadata.stop).toHaveBeenCalledTimes(1);
});

it('PRD-03-VS-01-T-02: an in-flight canonical response cannot restore a name after disconnection', async () => {
  let release!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile)
    : new Promise<Response>(resolve => { release = resolve; })));
  mount(); await waitFor(() => expect(release).toBeDefined());
  disconnect();
  await act(async () => { release(reply(summary())); });
  expect(screen.queryByText('Current council')).toBeNull();
  expect(screen.getByText('Organization', { exact: true })).toBeVisible();
  expect(metadata.watch).not.toHaveBeenCalled();
});

it('PRD-03-VS-01-T-02: refuses offline refreshes and requires fresh authorized reads on reconnection', async () => {
  let denied = false, freshName = 'Current council';
  const fetcher = vi.fn(async (path: string) => path === '/me' ? reply(profile)
    : denied ? reply({ code: 'not_found' }, 404) : reply(summary(freshName)));
  vi.stubGlobal('fetch', fetcher); mount(); await screen.findByText('Current council');
  const subscription = metadata.watch.mock.calls[0][0];
  disconnect(); const reads = fetcher.mock.calls.length;
  await act(async () => { fireEvent.focus(window); subscription.invalidate(); subscription.reset(); subscription.unavailable(); });
  expect(fetcher).toHaveBeenCalledTimes(reads);
  expect(screen.queryByText('Current council')).toBeNull();
  denied = true; reconnect();
  await waitFor(() => expect(fetcher.mock.calls.length).toBeGreaterThan(reads));
  await act(async () => {});
  expect(screen.getByText('Organization', { exact: true })).toBeVisible();
  denied = false; freshName = 'Freshly authorized council';
  act(() => { fireEvent.focus(window); });
  await screen.findByText(freshName);
  expect(screen.queryByText('Current council')).toBeNull();
  const canonical = fetcher.mock.calls.filter(([path]) => path === `/organizations/${organization}`);
  expect(canonical).toHaveLength(3);
});
