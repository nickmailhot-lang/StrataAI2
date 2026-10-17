import { act, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { SurfaceAdmission } from './SurfaceAdmission';
import type { watchOperationalLifecycle } from '../features/organizations/operationalLifecycleLive';

const live = vi.hoisted(() => ({ options: undefined as Parameters<typeof watchOperationalLifecycle>[0] | undefined }));
vi.mock('../features/organizations/operationalLifecycleLive', () => ({
  watchOperationalLifecycle: (options: Parameters<typeof watchOperationalLifecycle>[0]) => { live.options = options; return vi.fn(); },
}));
const organization = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', card = '44444444-4444-4444-8444-444444444444';
const boardPath = `/app/${organization}/boards/${board}`, cardPath = `${boardPath}/cards/${card}`;
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
beforeEach(() => { live.options = undefined; });
afterEach(() => vi.unstubAllGlobals());

function mount(path: string) {
  const router = createMemoryRouter([
    { path: '/app/:organizationId/boards/:boardId/*', element: <SurfaceAdmission surface="INTERNAL"
      deniedContent={<div>Independent Board view</div>}><div>Private Board content</div></SurfaceAdmission> },
    { path: '/app/:organizationId/settings', element: <SurfaceAdmission surface="INTERNAL"><div>Private settings</div></SurfaceAdmission> },
    { path: '/portal/:organizationId', element: <SurfaceAdmission surface="PORTAL"><div>Private Portal content</div></SurfaceAdmission> },
    { path: '/login', element: <div>Fresh sign-in required</div> },
  ], { initialEntries: [path] });
  render(<RouterProvider router={router} />); return router;
}

for (const path of [boardPath, cardPath, cardPath + '?private=discard#private']) {
  it(`PRD-01-VS-04: fresh session refusal retains only the approved current route ${path}`, async () => {
    let gone = false;
    vi.stubGlobal('fetch', vi.fn(async (url: string) => url === '/me'
      ? gone ? reply({}, 401) : reply(profile)
      : reply({ organizationId: organization, surface: 'INTERNAL' })));
    const router = mount(path); await screen.findByText('Private Board content');
    await waitFor(() => expect(live.options).toBeDefined()); gone = true;
    act(() => live.options!.denied(401, false));
    expect(screen.queryByText('Private Board content')).not.toBeInTheDocument();
    await screen.findByText('Fresh sign-in required');
    expect(router.state.location.state).toEqual({ returnTo: path.split(/[?#]/)[0] });
  });
}

it('PRD-01-VS-04: a held session refusal returns to the current same-Organization Card, not an earlier Board', async () => {
  let gone = false, release!: (response: Response) => void;
  const held = new Promise<Response>(resolve => { release = resolve; });
  const fetcher = vi.fn(async (url: string) => url === '/me'
    ? gone ? held : reply(profile) : reply({ organizationId: organization, surface: 'INTERNAL' }));
  vi.stubGlobal('fetch', fetcher); const router = mount(boardPath);
  await screen.findByText('Private Board content'); await waitFor(() => expect(live.options).toBeDefined()); gone = true;
  act(() => live.options!.denied(401, false));
  await waitFor(() => expect(fetcher.mock.calls.filter(([url]) => url === '/me').length).toBeGreaterThanOrEqual(2));
  await act(async () => { await router.navigate(cardPath); });
  await act(async () => { release(reply({}, 401)); });
  await screen.findByText('Fresh sign-in required');
  expect(router.state.location.state).toEqual({ returnTo: cardPath });
});

for (const path of [`/app/${organization}/settings`, `/portal/${organization}`]) {
  it(`PRD-01-VS-04: session refusal does not turn an unsupported or separate surface into a Board return ${path}`, async () => {
    vi.stubGlobal('fetch', vi.fn(async () => reply({}, 401)));
    const router = mount(path); await screen.findByText('Fresh sign-in required');
    expect(router.state.location.state?.returnTo).toBeUndefined();
  });
}
