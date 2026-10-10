import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { SurfaceAdmission } from './SurfaceAdmission';
import { useState } from 'react';
import { Dialog } from '@mui/material';

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
