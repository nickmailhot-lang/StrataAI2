import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { SurfaceAdmission } from './SurfaceAdmission';
import { useState } from 'react';
import { Dialog } from '@mui/material';
import type { watchOperationalLifecycle } from '../features/organizations/operationalLifecycleLive';
const operational = vi.hoisted(() => ({ options: undefined as Parameters<typeof watchOperationalLifecycle>[0] | undefined,
  stop: vi.fn() }));
vi.mock('../features/organizations/operationalLifecycleLive', () => ({
  watchOperationalLifecycle: (options: Parameters<typeof watchOperationalLifecycle>[0]) => { operational.options = options; return operational.stop; },
}));
beforeEach(() => { operational.options = undefined; operational.stop.mockClear(); });

const liveOrg = '55555555-5555-4555-8555-555555555555', liveActor = '22222222-2222-4222-8222-222222222222';
const liveProfile = { id: liveActor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const livePage = { organizationId: liveOrg, actorId: liveActor, status: 'ACTIVE' as const, version: 3, afterVersion: 0,
  nextVersion: null, pending: false, events: [] };
function mountLive(surface: 'INTERNAL' | 'PORTAL' = 'INTERNAL') {
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface={surface}><Content /></SurfaceAdmission> }],
    { initialEntries: [`/${liveOrg}`] }); render(<RouterProvider router={router} />); return router;
}
it.each(['SUSPENDED', 'DEACTIVATED'] as const)('withdraws operational content immediately for live %s and checks fresh admission on reactivation', async status => {
  const fetcher = vi.fn(async (path: string) => path === '/me' ? new Response(JSON.stringify(liveProfile)) : response(liveOrg));
  vi.stubGlobal('fetch', fetcher); mountLive(); await screen.findByText(`Admitted ${liveOrg}`);
  await waitFor(() => expect(operational.options).toBeDefined());
  act(() => operational.options!.update({ ...livePage, status }));
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
  await act(async () => { await operational.options!.admit(livePage, new AbortController().signal); operational.options!.update(livePage); });
  expect(await screen.findByText(`Admitted ${liveOrg}`)).toBeVisible();
  expect(fetcher.mock.calls.filter(([path]) => path.includes('/surface-access'))).toHaveLength(2);
});
it('does not use internal lifecycle streams to admit the separate Portal', async () => {
  const fetcher = vi.fn(async () => response(liveOrg, 'PORTAL')); vi.stubGlobal('fetch', fetcher); mountLive('PORTAL');
  expect(await screen.findByText(`Admitted ${liveOrg}`)).toBeVisible(); expect(operational.options).toBeUndefined();
  expect(fetcher).toHaveBeenCalledTimes(1);
});
it('fences an older in-flight polling grant after live suspension', async () => {
  let calls = 0; let finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? new Response(JSON.stringify(liveProfile))
    : ++calls === 2 ? new Promise<Response>(resolve => { finish = resolve; }) : response(liveOrg)));
  mountLive(); await screen.findByText(`Admitted ${liveOrg}`); await waitFor(() => expect(operational.options).toBeDefined());
  fireEvent.focus(window); await waitFor(() => expect(finish).toBeDefined());
  act(() => operational.options!.update({ ...livePage, status: 'SUSPENDED' }));
  await act(async () => finish(response(liveOrg)));
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
  expect(screen.getByText('Access to this Organization surface is unavailable.')).toBeInTheDocument();
});
it('keeps failed live transport hidden and withdraws it permanently on fresh denial', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? new Response(JSON.stringify(liveProfile)) : response(liveOrg)));
  mountLive(); await screen.findByText(`Admitted ${liveOrg}`); await waitFor(() => expect(operational.options).toBeDefined());
  act(() => operational.options!.unavailable()); expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeVisible();
  act(() => operational.options!.denied(404, false)); expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
});

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
function Content() { const { organizationId } = useParams(); return <div>Admitted {organizationId}</div>; }
function mount(surface: 'INTERNAL' | 'PORTAL' = 'INTERNAL', fallback = false) {
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface={surface}
    deniedContent={fallback ? <div>Independent Board view</div> : undefined}><Content /></SurfaceAdmission> }], { initialEntries: ['/first'] });
  render(<RouterProvider router={router} />); return router;
}
const response = (organizationId: string, surface = 'INTERNAL') => new Response(JSON.stringify({ organizationId, surface }));

describe('ARCH-02 current surface admission', () => {
  it.each(['INTERNAL', 'PORTAL'] as const)('admits uppercase UUID routes using the canonical response without crossing surfaces (%s)', async surface => {
    const id = 'abcdefab-cdef-4abc-8def-abcdefabcdef';
    const fetcher = vi.fn((url: string) => { void url; return Promise.resolve(response(id, surface)); });
    vi.stubGlobal('fetch', fetcher);
    const router = createMemoryRouter([{ path: '/:organizationId', element:
      <SurfaceAdmission surface={surface}><Content /></SurfaceAdmission> }], { initialEntries: [`/${id.toUpperCase()}`] });
    render(<RouterProvider router={router} />);
    expect(await screen.findByText(`Admitted ${id.toUpperCase()}`)).toBeVisible();
    expect(fetcher.mock.calls[0][0]).toBe(`/organizations/${id}/surface-access?surface=${surface}`);
    fetcher.mockImplementation(() => Promise.resolve(response(id, surface === 'INTERNAL' ? 'PORTAL' : 'INTERNAL')));
    fireEvent.focus(window);
    expect(await screen.findByText('Access could not be checked. Try again.')).toBeInTheDocument();
    expect(screen.queryByText(`Admitted ${id.toUpperCase()}`)).not.toBeVisible();
  });

  it('hides a retained MUI dialog and keeps its draft through explicit transport recovery', async () => {
    vi.useFakeTimers();
    function Draft() {
      const [value, setValue] = useState('');
      return <Dialog open slotProps={{ paper: { 'aria-label': 'Protected draft' } }}><input aria-label="Original draft" value={value} onChange={event => setValue(event.target.value)} /></Dialog>;
    }
    let checks = 0;
    vi.stubGlobal('fetch', vi.fn(async () => ++checks === 2 ? new Response('{}', { status: 503 }) : response('first')));
    const router = createMemoryRouter([{ path: '/:organizationId', element:
      <SurfaceAdmission surface="INTERNAL"><Draft /></SurfaceAdmission> }], { initialEntries: ['/first'] });
    await act(async () => { render(<RouterProvider router={router} />); await vi.advanceTimersByTimeAsync(0); });
    fireEvent.change(screen.getByRole('textbox', { name: 'Original draft' }), { target: { value: 'Unsent original' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
    expect(screen.queryByRole('dialog', { name: 'Protected draft' })).not.toBeInTheDocument();
    const check = screen.getByRole('button', { name: 'Check access again' });
    expect(check).toBeVisible(); check.focus(); expect(check).toHaveFocus();
    await act(async () => { fireEvent.click(check); await vi.advanceTimersByTimeAsync(0); });
    expect(screen.getByRole('dialog', { name: 'Protected draft' })).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Original draft' })).toHaveValue('Unsent original');
  });
  it.each([503, 403])('withdraws surface content during failed checks and preserves recovery only for transient failure (%s)', async status => {
    vi.useFakeTimers();
    function Recovery() {
      const [count, setCount] = useState(0);
      return <button onClick={() => setCount(value => value + 1)}>Original recovery {count}</button>;
    }
    let checks = 0;
    vi.stubGlobal('fetch', vi.fn(async () => ++checks === 2 ? new Response('{}', { status }) : response('first')));
    const router = createMemoryRouter([{ path: '/:organizationId', element:
      <SurfaceAdmission surface="INTERNAL"><Recovery /></SurfaceAdmission> }], { initialEntries: ['/first'] });
    await act(async () => { render(<RouterProvider router={router} />); await vi.advanceTimersByTimeAsync(0); });
    fireEvent.click(screen.getByRole('button', { name: 'Original recovery 0' }));
    expect(screen.getByRole('button', { name: 'Original recovery 1' })).toBeVisible();
    await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
    expect(screen.queryByRole('button', { name: 'Original recovery 1' })).not.toBeInTheDocument();
    if (status === 403) expect(screen.queryByText('Original recovery 1')).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
    expect(screen.getByRole('button', { name: `Original recovery ${status === 503 ? 1 : 0}` })).toBeVisible();
  });
  it('withholds protected content before admission and after denial', async () => {
    let finish!: (value: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })));
    mount(); expect(screen.getByLabelText('Checking Organization access')).toBeInTheDocument();
    expect(screen.queryByText('Admitted first')).not.toBeInTheDocument();
    await act(async () => finish(new Response('{}', { status: 404 })));
    expect(await screen.findByText('Access to this Organization surface is unavailable.')).toBeInTheDocument();
    expect(screen.queryByText('Admitted first')).not.toBeInTheDocument();
  });
  it('does not accept an internal response as a Portal grant', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(response('first'))));
    mount('PORTAL');
    expect(await screen.findByText('Access could not be checked. Try again.')).toBeInTheDocument();
    expect(screen.queryByText('Admitted first')).not.toBeInTheDocument();
  });
  it('retires old Organization replies even if transport ignores abort', async () => {
    const replies: ((value: Response) => void)[] = [];
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => replies.push(resolve))));
    const router = mount();
    await act(async () => { await router.navigate('/second'); });
    await act(async () => replies[0](response('first')));
    expect(screen.queryByText('Admitted first')).not.toBeInTheDocument();
    expect(screen.queryByText('Admitted second')).not.toBeInTheDocument();
    await act(async () => replies[1](response('second')));
    expect(await screen.findByText('Admitted second')).toBeInTheDocument();
  });
  it('removes admitted content after a current revocation check on focus', async () => {
    const fetch = vi.fn().mockImplementationOnce(() => Promise.resolve(response('first')))
      .mockImplementation(() => Promise.resolve(new Response('{}', { status: 404 })));
    vi.stubGlobal('fetch', fetch); mount();
    expect(await screen.findByText('Admitted first')).toBeInTheDocument();
    fireEvent.focus(window);
    expect(await screen.findByText('Access to this Organization surface is unavailable.')).toBeInTheDocument();
    expect(screen.queryByText('Admitted first')).not.toBeInTheDocument();
  });
  it('keeps independently authorized Board viewing separate from protected shell content', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('{}', { status: 401 }))));
    mount('INTERNAL', true);
    expect(await screen.findByText('Independent Board view')).toBeInTheDocument();
    expect(screen.queryByText('Admitted first')).not.toBeInTheDocument();
  });
  it('bounds unresponsive admission and supports an explicit fresh check', async () => {
    vi.useFakeTimers();
    const fetch = vi.fn().mockImplementationOnce(() => new Promise<Response>(() => {}))
      .mockImplementation(() => Promise.resolve(response('first')));
    vi.stubGlobal('fetch', fetch); mount();
    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
    expect(screen.getByText('Access could not be checked. Try again.')).toBeInTheDocument();
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Check access again' })));
    expect(screen.getByText('Admitted first')).toBeInTheDocument();
  });
});
