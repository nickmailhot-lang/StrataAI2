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
it.each([true,false])('confirms session loss after the outer boundary retires private children (%s)',async sessionGone=>{
  let gone=false;
  vi.stubGlobal('fetch',vi.fn(async(path:string)=>path==='/me'
    ? gone && sessionGone ? new Response('{}',{status:401}) : new Response(JSON.stringify(liveProfile)) : response(liveOrg)));
  const router=createMemoryRouter([{path:'/:organizationId',element:<SurfaceAdmission surface="INTERNAL"><div>Private child draft</div></SurfaceAdmission>},
    {path:'/login',element:<div>Sign in again</div>}],{initialEntries:[`/${liveOrg}`]});render(<RouterProvider router={router}/>);
  await screen.findByText('Private child draft');await waitFor(()=>expect(operational.options).toBeDefined());gone=true;
  act(()=>operational.options!.denied(sessionGone?401:404,false));
  expect(screen.queryByText('Private child draft')).not.toBeInTheDocument();
  if(sessionGone){await screen.findByText('Sign in again');expect(router.state.location.pathname).toBe('/login');}
  else{await screen.findByText('Access to this Organization surface is unavailable.');expect(router.state.location.pathname).toBe(`/${liveOrg}`);}
});
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
it('retains independently authorized recovery after surface denial and an operational stream transport failure', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
    ? new Response(JSON.stringify(liveProfile)) : new Response('{}', { status: 404 })));
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface="INTERNAL"
    deniedContent={<div>Independent terminal recovery</div>}><Content /></SurfaceAdmission> }],
  { initialEntries: [`/${liveOrg}`] });
  render(<RouterProvider router={router} />);
  await screen.findByText('Independent terminal recovery');
  await waitFor(() => expect(operational.options).toBeDefined());
  act(() => operational.options!.unavailable());
  expect(screen.getByText('Independent terminal recovery')).toBeVisible();
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
  expect(screen.queryByText('Access could not be checked. Try again.')).not.toBeInTheDocument();
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
it.each([200, 404])('finishes initial HTTP admission after an earlier stream transport failure (%s)', async status => {
  let finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
    ? new Response(JSON.stringify(liveProfile)) : new Promise<Response>(resolve => { finish = resolve; })));
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface="INTERNAL"
    deniedContent={<div>Independent Board view</div>}><Content /></SurfaceAdmission> }],
  { initialEntries: [`/${liveOrg}`] });
  render(<RouterProvider router={router} />);
  await waitFor(() => expect(operational.options).toBeDefined());
  act(() => operational.options!.unavailable());
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
  expect(screen.queryByText('Independent Board view')).not.toBeInTheDocument();
  await act(async () => finish(status === 200 ? response(liveOrg) : new Response('{}', { status })));
  expect(screen.getByText(status === 200 ? `Admitted ${liveOrg}` : 'Independent Board view')).toBeVisible();
  expect(screen.queryByText('Access could not be checked. Try again.')).not.toBeInTheDocument();
});
it('keeps failed live transport hidden and withdraws it permanently on fresh denial', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? new Response(JSON.stringify(liveProfile)) : response(liveOrg)));
  mountLive(); await screen.findByText(`Admitted ${liveOrg}`); await waitFor(() => expect(operational.options).toBeDefined());
  act(() => operational.options!.unavailable()); expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeVisible();
  act(() => operational.options!.denied(404, false)); expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
});

it('finishes an in-flight authority denial despite repeated stream uncertainty after admission', async () => {
  let calls = 0; let finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
    ? new Response(JSON.stringify(liveProfile)) : ++calls === 1 ? response(liveOrg)
      : new Promise<Response>(resolve => { finish = resolve; })));
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface="INTERNAL"
    deniedContent={<div>Independent lifecycle recovery</div>}><Content /></SurfaceAdmission> }],
  { initialEntries: [`/${liveOrg}`] }); render(<RouterProvider router={router} />);
  await screen.findByText(`Admitted ${liveOrg}`);
  await waitFor(() => expect(operational.options).toBeDefined());
  fireEvent.focus(window); await waitFor(() => expect(finish).toBeDefined());
  act(() => { operational.options!.unavailable(); operational.options!.unavailable(); });
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeVisible();
  await act(async () => finish(new Response('{}', { status: 404 })));
  expect(screen.getByText('Independent lifecycle recovery')).toBeVisible();
  expect(screen.queryByText('Access could not be checked. Try again.')).not.toBeInTheDocument();
});

it('immediately rechecks current surface authority when an admitted live stream becomes unavailable', async () => {
  let calls = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
    ? new Response(JSON.stringify(liveProfile)) : ++calls === 1 ? response(liveOrg)
      : new Response('{}', { status: 404 })));
  mountLive(); await screen.findByText(`Admitted ${liveOrg}`);
  await waitFor(() => expect(operational.options).toBeDefined());
  act(() => operational.options!.unavailable());
  expect(await screen.findByText('Access to this Organization surface is unavailable.')).toBeVisible();
  expect(calls).toBe(2);
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
});

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
function Content() { const { organizationId } = useParams(); return <div>Admitted {organizationId}</div>; }
function mount(surface: 'INTERNAL' | 'PORTAL' = 'INTERNAL', fallback = false) {
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface={surface}
    deniedContent={fallback ? <div>Independent Board view</div> : undefined}><Content /></SurfaceAdmission> }], { initialEntries: ['/first'] });
  render(<RouterProvider router={router} />); return router;
}
const response = (organizationId: string, surface = 'INTERNAL') => new Response(JSON.stringify({ organizationId, surface }));

it.each([true, false])('does not recheck unchanged authority when only fallback UI changes (admitted: %s)', async initiallyAdmitted => {
  let granted = initiallyAdmitted;
  const fetcher = vi.fn(async (path: string) => path === '/me'
    ? new Response(JSON.stringify(liveProfile)) : granted ? response(liveOrg) : new Response('{}', { status: 404 }));
  vi.stubGlobal('fetch', fetcher);
  function Harness() {
    const [revision, setRevision] = useState(0);
    return <><button onClick={() => setRevision(value => value + 1)}>Update fallback presentation</button>
      <SurfaceAdmission surface="INTERNAL" deniedContent={<div>Independent Board fallback {revision}</div>}><Content /></SurfaceAdmission></>;
  }
  const router = createMemoryRouter([{ path: '/:organizationId', element: <Harness /> }], { initialEntries: [`/${liveOrg}`] });
  render(<RouterProvider router={router} />);
  await screen.findByText(initiallyAdmitted ? `Admitted ${liveOrg}` : 'Independent Board fallback 0');
  const before = fetcher.mock.calls.length;
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Update fallback presentation' })));
  expect(fetcher).toHaveBeenCalledTimes(before);
  granted = false;
  fireEvent.focus(window);
  await screen.findByText('Independent Board fallback 1');
  expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
  expect(fetcher).toHaveBeenCalledTimes(before + 1);
});

it('checks the anonymous session when same-scope navigation removes the independent fallback', async () => {
  const fetcher = vi.fn<(path: string) => Promise<Response>>(async () => new Response('{}', { status: 401 }));
  vi.stubGlobal('fetch', fetcher);
  function Harness() {
    const [fallback, setFallback] = useState(true);
    return <><button onClick={() => setFallback(false)}>Open protected area</button>
      <SurfaceAdmission surface="INTERNAL" deniedContent={fallback ? <div>Independent public Board</div> : undefined}>
        <Content />
      </SurfaceAdmission></>;
  }
  const router = createMemoryRouter([{ path: '/:organizationId', element: <Harness /> },
    { path: '/login', element: <div>Fresh sign-in required</div> }], { initialEntries: [`/${liveOrg}`] });
  render(<RouterProvider router={router} />);
  await screen.findByText('Independent public Board');
  // The independent lifecycle observer may already have checked the profile.
  // Entering the protected area must perform an additional fresh session check.
  const profileReads = fetcher.mock.calls.filter(call => call[0] === '/me').length;
  fireEvent.click(screen.getByRole('button', { name: 'Open protected area' }));
  await screen.findByText('Fresh sign-in required');
  expect(router.state.location.pathname).toBe('/login');
  expect(fetcher.mock.calls.filter(call => call[0] === '/me')).toHaveLength(profileReads + 1);
  expect(screen.queryByText('Independent public Board')).not.toBeInTheDocument();
});

it('clears this Organization private policy recovery when the outer boundary withdraws the page', async () => {
  const key = `strataai:retention-policy-change:v1:${liveActor}:${liveOrg}`;
  const other = `strataai:retention-policy-change:v1:${liveActor}:44444444-4444-4444-8444-444444444444`;
  sessionStorage.setItem(key, 'unresolved private original'); sessionStorage.setItem(other, 'other Organization original');
  const sourceKey = `strataai:retention-source-classification:v1:${liveActor}:${liveOrg}`;
  const sourceOther = `strataai:retention-source-classification:v1:${liveActor}:44444444-4444-4444-8444-444444444444`;
  sessionStorage.setItem(sourceKey, 'original source evidence');
  const holdKey = `strataai:hold-command:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(holdKey, 'original hold evidence'); sessionStorage.setItem(sourceOther, 'other source evidence');
  const otherHold = `strataai:hold-command:v1:${liveActor}:44444444-4444-4444-8444-444444444444`;
  sessionStorage.setItem(otherHold, 'other hold evidence');
  try {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? new Response(JSON.stringify(liveProfile)) : response(liveOrg)));
    mountLive(); await screen.findByText(`Admitted ${liveOrg}`); await waitFor(() => expect(operational.options).toBeDefined());
    act(() => operational.options!.denied(404, false));
    expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeInTheDocument();
    await waitFor(() => expect(sessionStorage.getItem(key)).toBeNull());
    expect(sessionStorage.getItem(sourceKey)).toBeNull();
    expect(sessionStorage.getItem(holdKey)).toBeNull();
    expect(sessionStorage.getItem(sourceOther)).toBe('other source evidence');
    expect(sessionStorage.getItem(otherHold)).toBe('other hold evidence');
    expect(sessionStorage.getItem(other)).toBe('other Organization original');
  } finally { sessionStorage.clear(); }
});
it('preserves unresolved policy recovery through transport uncertainty until fresh authority resolves', async () => {
  const key = `strataai:retention-policy-change:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(key, 'unresolved private original'); let calls = 0;
  const sourceKey = `strataai:retention-source-classification:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(sourceKey, 'original source evidence');
  const holdKey = `strataai:hold-command:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(holdKey, 'original hold evidence');
  try {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? new Response(JSON.stringify(liveProfile))
      : ++calls === 1 ? response(liveOrg) : new Promise<Response>(() => {})));
    mountLive(); await screen.findByText(`Admitted ${liveOrg}`); await waitFor(() => expect(operational.options).toBeDefined());
    act(() => operational.options!.unavailable());
    expect(screen.queryByText(`Admitted ${liveOrg}`)).not.toBeVisible();
    expect(sessionStorage.getItem(key)).toBe('unresolved private original');
    expect(sessionStorage.getItem(sourceKey)).toBe('original source evidence');
    expect(sessionStorage.getItem(holdKey)).toBe('original hold evidence');
  } finally { sessionStorage.clear(); }
});
it('does not discard internal policy recovery when the separate Portal is denied', async () => {
  const key = `strataai:retention-policy-change:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(key, 'unresolved internal original');
  const sourceKey = `strataai:retention-source-classification:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(sourceKey, 'original source evidence');
  const holdKey = `strataai:hold-command:v1:${liveActor}:${liveOrg}`;
  sessionStorage.setItem(holdKey, 'original hold evidence');
  try {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 404 }))); mountLive('PORTAL');
    await screen.findByText('Access to this Organization surface is unavailable.');
    expect(sessionStorage.getItem(key)).toBe('unresolved internal original');
    expect(sessionStorage.getItem(sourceKey)).toBe('original source evidence');
    expect(sessionStorage.getItem(holdKey)).toBe('original hold evidence');
  } finally { sessionStorage.clear(); }
});

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
