import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { SurfaceAdmission } from './SurfaceAdmission';

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
function Content() { const { organizationId } = useParams(); return <div>Admitted {organizationId}</div>; }
function mount(surface: 'INTERNAL' | 'PORTAL' = 'INTERNAL', fallback = false) {
  const router = createMemoryRouter([{ path: '/:organizationId', element: <SurfaceAdmission surface={surface}
    deniedContent={fallback ? <div>Independent Board view</div> : undefined}><Content /></SurfaceAdmission> }], { initialEntries: ['/first'] });
  render(<RouterProvider router={router} />); return router;
}
const response = (organizationId: string, surface = 'INTERNAL') => new Response(JSON.stringify({ organizationId, surface }));

describe('ARCH-02 current surface admission', () => {
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
