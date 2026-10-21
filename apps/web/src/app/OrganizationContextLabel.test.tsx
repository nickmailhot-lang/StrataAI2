import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { AppThemeFixture } from '../test/AppThemeFixture';
import { InternalAppShell } from './InternalAppShell';

type MetadataOptions = Parameters<typeof import('../features/organizations/organizationMetadataLive').watchOrganizationMetadata>[0];
const metadata = vi.hoisted(() => ({ watch: vi.fn<(options: MetadataOptions) => () => void>(() => () => {}) }));
vi.mock('./SurfaceAdmission', () => ({ SurfaceAdmission: ({ children }: { children: ReactNode }) => children }));
vi.mock('./NavigationConfirmation', () => ({ NavigationConfirmation: () => null }));
vi.mock('../features/kanban/CardDescriptionPreview', () => ({ DescriptionParserWarmup: () => null }));
vi.mock('../features/organizations/organizationMetadataLive', () => ({ watchOrganizationMetadata: metadata.watch }));

const organization = '11111111-1111-4111-8111-111111111111';
const otherOrganization = '33333333-3333-4333-8333-333333333333';
const actor = '22222222-2222-4222-8222-222222222222';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const summary = (id = organization, name = 'Cedar Court') => ({ organization: { id, name, description: null, status: 0 }, role: 2 });
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });

beforeEach(() => {
  metadata.watch.mockClear();
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

function mount(id = organization) {
  const router = createMemoryRouter([{ path: '/app/:organizationId', element: <InternalAppShell />,
    children: [{ path: '*', element: <h2>Current work</h2> }] }], { initialEntries: [`/app/${id}/boards/board`] });
  render(<AppThemeFixture><RouterProvider router={router} /></AppThemeFixture>);
  return router;
}

it('PRD-03-VS-01-T-02: identifies the current Organization by its authorized name rather than a technical identifier', async () => {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me' ? reply(profile) : reply(summary()));
  vi.stubGlobal('fetch', fetcher);
  mount();
  expect(await within(screen.getByRole('banner')).findByText('Cedar Court')).toBeVisible();
  expect(screen.getByRole('banner')).not.toHaveTextContent(organization);
  const read = fetcher.mock.calls.find(([path]) => path === `/organizations/${organization}`);
  expect(read).toBeDefined();
  expect(new Headers(read![1]?.headers).get('X-StrataAI-Expected-Actor')).toBe(actor);
  expect(within(screen.getByRole('banner')).getByTitle('Cedar Court')).toHaveTextContent('Organization: Cedar Court');
});

it.each<[string, unknown]>([
  ['another Organization', summary(otherOrganization)],
  ['an invalid Organization identity', { ...summary(), organization: { ...summary().organization, id: 'foreign' } }],
  ['an inactive Organization', { ...summary(), organization: { ...summary().organization, status: 1 } }],
  ['a role outside the authorized contract', { ...summary(), role: 3 }],
  ['a blank name', summary(organization, ' ')],
  ['an oversized name', summary(organization, 'x'.repeat(161))],
])('PRD-03-VS-01-T-02: never discloses a name from %s', async (_label, value) => {
  const fetcher = vi.fn(async (path: string) => path === '/me' ? reply(profile) : reply(value));
  vi.stubGlobal('fetch', fetcher);
  mount();
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2));
  await act(async () => {});
  expect(within(screen.getByRole('banner')).getByText('Organization', { exact: true })).toBeVisible();
  expect(within(screen.getByRole('banner')).queryByTitle('Cedar Court')).toBeNull();
});

it.each([401, 403, 404, 503])('PRD-03-VS-01-T-02: withdraws the name after a canonical read refuses with %s', async status => {
  let refused = false;
  let release!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile)
    : refused ? new Promise<Response>(resolve => { release = resolve; }) : reply(summary())));
  mount();
  await screen.findByText('Cedar Court');
  refused = true;
  act(() => fireEvent.focus(window));
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  await waitFor(() => expect(release).toBeDefined());
  await act(async () => release(reply({ ...summary(), detail: 'Private response must not appear' }, status)));
  expect(within(screen.getByRole('banner')).getByText('Organization', { exact: true })).toBeVisible();
  expect(screen.getByRole('banner')).not.toHaveTextContent('Private response');
  expect(within(screen.getByRole('banner')).queryByTitle('Cedar Court')).toBeNull();
});

it('PRD-03-VS-01-T-02: refuses a name when the signed-in account changes between admission reads', async () => {
  let reads = 0;
  const replacement = { ...profile, id: '44444444-4444-4444-8444-444444444444' };
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
    ? reply(++reads === 1 ? profile : replacement) : reply(summary())));
  mount();
  await waitFor(() => expect(reads).toBe(2));
  await act(async () => {});
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  expect(within(screen.getByRole('banner')).queryByTitle('Cedar Court')).toBeNull();
});

it('PRD-03-VS-01-T-02: ignores an obsolete response after the new Organization is admitted', async () => {
  let oldResponse!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return reply(profile);
    if (path === `/organizations/${organization}`) return new Promise<Response>(resolve => { oldResponse = resolve; });
    if (path === `/organizations/${otherOrganization}`) return reply(summary(otherOrganization, 'Maple Court'));
    throw new Error('Unexpected request');
  }));
  const router = mount();
  await waitFor(() => expect(oldResponse).toBeDefined());
  await act(async () => { await router.navigate(`/app/${otherOrganization}/boards/other`); });
  await screen.findByText('Maple Court');
  await act(async () => oldResponse(reply(summary())));
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  expect(within(screen.getByRole('banner')).getByTitle('Maple Court')).toHaveTextContent('Organization: Maple Court');
});

it.each(['invalidate', 'reset', 'unavailable'] as const)('PRD-03-VS-01-T-02: metadata %s removes the old name before fresh authority returns', async event => {
  let updating = false;
  let release!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile)
    : updating ? new Promise<Response>(resolve => { release = resolve; }) : reply(summary())));
  mount();
  await screen.findByText('Cedar Court');
  const subscription = metadata.watch.mock.calls.at(-1)![0];
  expect(subscription.organizationId).toBe(organization); expect(subscription.userId).toBe(actor);
  updating = true;
  act(() => subscription[event]());
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  expect(within(screen.getByRole('banner')).queryByTitle('Cedar Court')).toBeNull();
  await waitFor(() => expect(release).toBeDefined());
  await act(async () => release(reply(summary(organization, 'Updated Court'))));
  expect(await screen.findByText('Updated Court')).toBeVisible();
});

it('PRD-03-VS-01-T-02: preserves the full canonical long name without clipping it', async () => {
  const longName = 'North garden '.repeat(13).slice(0, 159) + 'X';
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile) : reply(summary(organization, longName))));
  mount();
  const caption = await screen.findByTitle(longName);
  expect(longName).toHaveLength(160);
  expect(caption).toHaveTextContent(longName);
  expect(caption).not.toHaveStyle({ overflow: 'hidden' });
  expect(caption).not.toHaveStyle({ whiteSpace: 'nowrap' });
});

it('PRD-03-VS-01-T-02: legacy or invalid route identifiers remain neutral without a protected lookup', () => {
  const fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher);
  mount('demo');
  expect(within(screen.getByRole('banner')).getByText('Organization', { exact: true })).toBeVisible();
  expect(screen.getByRole('banner')).not.toHaveTextContent('demo');
  expect(fetcher).not.toHaveBeenCalled();
});

it.each([0, 1])('PRD-03-VS-01-T-02: identifies the canonical Organization for authorized role %s', async role => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile) : reply({ ...summary(), role })));
  mount();
  expect(await screen.findByText('Cedar Court')).toBeVisible();
});

it('PRD-03-VS-01-T-02: reentering a previous Organization requires fresh authority rather than its old name', async () => {
  let reentering = false;
  let refused!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return reply(profile);
    if (path === `/organizations/${otherOrganization}`) return reply(summary(otherOrganization, 'Maple Court'));
    return reentering ? new Promise<Response>(resolve => { refused = resolve; }) : reply(summary());
  }));
  const router = mount();
  await screen.findByText('Cedar Court');
  await act(async () => { await router.navigate(`/app/${otherOrganization}/boards/other`); });
  await screen.findByText('Maple Court');
  reentering = true;
  await act(async () => { await router.navigate(`/app/${organization}/boards/board`); });
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  expect(screen.getByRole('banner')).not.toHaveTextContent('Maple Court');
  await waitFor(() => expect(refused).toBeDefined());
  await act(async () => refused(reply({ code: 'organization_unavailable' }, 403)));
  expect(within(screen.getByRole('banner')).getByText('Organization', { exact: true })).toBeVisible();
});

it('PRD-03-VS-01-T-02: a hidden document withdraws context and resumes with a fresh canonical name', async () => {
  let name = 'Cedar Court';
  const stop = vi.fn(); metadata.watch.mockReturnValueOnce(stop);
  const fetcher = vi.fn(async (path: string) => path === '/me' ? reply(profile) : reply(summary(organization, name)));
  vi.stubGlobal('fetch', fetcher);
  mount();
  await screen.findByText('Cedar Court');
  const previous = metadata.watch.mock.calls[0][0];
  const visible = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
  act(() => document.dispatchEvent(new Event('visibilitychange')));
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  expect(stop).toHaveBeenCalledOnce();
  const requests = fetcher.mock.calls.length;
  act(() => previous.invalidate());
  expect(fetcher).toHaveBeenCalledTimes(requests);
  name = 'Updated Court'; visible.mockReturnValue('visible');
  act(() => document.dispatchEvent(new Event('visibilitychange')));
  expect(await screen.findByText('Updated Court')).toBeVisible();
  expect(metadata.watch).toHaveBeenCalledTimes(2);
});

it('PRD-03-VS-01-T-02: uses a neutral label while current authorized context is unknown', () => {
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
  mount();
  expect(screen.getByRole('banner')).not.toHaveTextContent(organization);
  expect(within(screen.getByRole('banner')).getByText('Organization', { exact: true })).toBeVisible();
});

it('PRD-03-VS-01-T-02: cannot transfer the previous name into another Organization route while its read is pending', async () => {
  let finish!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return reply(profile);
    if (path === `/organizations/${organization}`) return reply(summary());
    if (path === `/organizations/${otherOrganization}`) return new Promise<Response>(resolve => { finish = resolve; });
    throw new Error('Unexpected request');
  }));
  const router = mount();
  await screen.findByText('Cedar Court');
  await act(async () => { await router.navigate(`/app/${otherOrganization}/boards/other`); });
  expect(screen.getByRole('banner')).not.toHaveTextContent('Cedar Court');
  expect(screen.getByRole('banner')).not.toHaveTextContent(otherOrganization);
  await waitFor(() => expect(finish).toBeDefined());
  await act(async () => finish(reply(summary(otherOrganization, 'Maple Court'))));
  expect(await screen.findByText('Maple Court')).toBeVisible();
});
