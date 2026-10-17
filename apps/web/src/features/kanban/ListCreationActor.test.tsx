import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardScreen } from './BoardScreen';
import { AppThemeFixture } from '../../test/AppThemeFixture';
import { watchBoard } from '../../api/boardLive';
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const actor = '11111111-1111-1111-1111-111111111111';
const profile = { id: actor, status: 'ACTIVE', version: 1, locale: 'en', timezone: 'UTC', emailVerified: true };
const snapshot = { board: { id: 'board', organizationId: 'org', name: 'Private List work', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [] };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount(outsideAction = false) {
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> }, { path: '/elsewhere', element: <p>Elsewhere</p> }], { initialEntries: ['/app/org/boards/board'] });
  render(<>{outsideAction && <button>Independent navigation</button>}<RouterProvider router={router} /></>, { wrapper: AppThemeFixture }); return router;
}
afterEach(() => { vi.unstubAllGlobals(); vi.mocked(watchBoard).mockReset(); });
describe('PRD-07-VS-05 List creation identity', () => {
  it('keeps a List form unavailable until its authenticated actor is confirmed', async () => {
    let finish!: (value: Response) => void;
    const fetcher = vi.fn(async (path: string) => path === '/me' ? new Promise<Response>(resolve => { finish = resolve; }) : reply(snapshot));
    vi.stubGlobal('fetch', fetcher); mount();
    const launcher = await screen.findByRole('button', { name: /^Add list$/ }); fireEvent.click(launcher);
    await waitFor(() => expect(finish).toBeDefined());
    expect(launcher).toBeDisabled(); expect(screen.queryByRole('dialog', { name: 'Add list' })).not.toBeInTheDocument();
    await act(async () => finish(reply(profile)));
    expect(await screen.findByLabelText(/List name/)).toBeVisible();
    expect(fetcher.mock.calls.filter(([path]) => path === '/me')).toHaveLength(1);
  });

  it('retains original actor/key/body on retry and focuses safe feedback after refusal', async () => {
    const writes: { path: string; key: string | null; body: string | null | undefined }[] = [];
    const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') return reply(profile);
      if (options?.method === 'POST') {
        writes.push({ path, key: new Headers(options.headers).get('Idempotency-Key'), body: options.body as string });
        if (writes.length === 1) throw new TypeError('Reply lost after commit');
        return reply({ code: 'session_unavailable' }, 401);
      }
      return reply(snapshot);
    });
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: /^Add list$/ }));
    fireEvent.change(await screen.findByLabelText(/List name/), { target: { value: 'Original List draft' } });
    const form = screen.getByRole('dialog', { name: 'Add list' });
    fireEvent.click(within(form).getByRole('button', { name: /^Create$/ }));
    expect(await within(form).findByRole('alert')).toHaveTextContent('Unable to confirm the save');
    fireEvent.click(within(form).getByRole('button', { name: /^Create$/ }));
    await waitFor(() => expect(screen.queryByLabelText(/List name/)).not.toBeInTheDocument());
    expect(screen.queryByText('Private List work')).not.toBeInTheDocument();
    expect(writes).toHaveLength(2); expect(writes[0].path).toBe(`/boards/board/lists?expectedActorId=${actor}`);
    expect(writes[1]).toEqual(writes[0]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
    expect(fetcher.mock.calls.filter(([path]) => path === '/me')).toHaveLength(1);
    expect(screen.getByRole('alert')).toHaveFocus();
  });

  it.each(['unavailable', 'invalid'] as const)('allows explicit entry retry after %s identity without exposing a List form', async scenario => {
    let reads = 0;
    const fetcher = vi.fn(async (path: string) => path === '/me'
      ? ++reads === 1 ? reply(scenario === 'invalid' ? { ...profile, id: 'not-an-actor' } : {}, scenario === 'invalid' ? 200 : 503) : reply(profile)
      : reply(snapshot));
    vi.stubGlobal('fetch', fetcher); mount();
    const launcher = await screen.findByRole('button', { name: /^Add list$/ }); fireEvent.click(launcher);
    expect(await screen.findByRole('alert')).toHaveTextContent('Service temporarily unavailable');
    expect(screen.queryByRole('dialog', { name: 'Add list' })).not.toBeInTheDocument();
    await waitFor(() => expect(launcher).toBeEnabled()); fireEvent.click(launcher);
    expect(await screen.findByLabelText(/List name/)).toBeVisible(); expect(reads).toBe(2);
  });

  it('does not open a late List form after a newer protected Board refusal', async () => {
    let invalidate!: () => void, finish!: (value: Response) => void, boardReads = 0;
    vi.mocked(watchBoard).mockImplementation(options => { invalidate = options.invalidate; return () => {}; });
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
      ? new Promise<Response>(resolve => { finish = resolve; }) : ++boardReads === 1 ? reply(snapshot) : reply({}, 403)));
    mount(); fireEvent.click(await screen.findByRole('button', { name: /^Add list$/ }));
    await waitFor(() => expect(finish).toBeDefined()); act(() => invalidate());
    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable');
    await act(async () => finish(reply(profile)));
    expect(screen.queryByLabelText(/List name/)).not.toBeInTheDocument(); expect(screen.queryByText('Private List work')).not.toBeInTheDocument();
  });

  it('cancels a pending List admission on route departure', async () => {
    let signal: AbortSignal | null | undefined;
    vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') { signal = options?.signal; return new Promise<Response>(() => {}); }
      return reply(snapshot);
    }));
    const router = mount(); fireEvent.click(await screen.findByRole('button', { name: /^Add list$/ }));
    await waitFor(() => expect(signal).toBeDefined()); await act(async () => { await router.navigate('/elsewhere'); });
    expect(signal?.aborted).toBe(true); expect(screen.getByText('Elsewhere')).toBeVisible();
  });

  it('preserves independent navigation focus when pending List admission is refused', async () => {
    let finish!: (value: Response) => void;
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
      ? new Promise<Response>(resolve => { finish = resolve; }) : reply(snapshot)));
    mount(true); fireEvent.click(await screen.findByRole('button', { name: /^Add list$/ }));
    await waitFor(() => expect(finish).toBeDefined());
    const navigation = screen.getByRole('button', { name: 'Independent navigation' }); navigation.focus();
    await act(async () => finish(reply({}, 401)));
    await waitFor(() => expect(screen.queryByText('Private List work')).not.toBeInTheDocument());
    expect(navigation).toHaveFocus(); expect(screen.queryByLabelText(/List name/)).not.toBeInTheDocument();
  });

  it('keeps List creation undiscoverable for a current read-only Board', async () => {
    const fetcher = vi.fn(async () => reply({ ...snapshot, access: { ...snapshot.access, canEdit: false } }));
    vi.stubGlobal('fetch', fetcher); mount(); await screen.findByRole('heading', { name: 'Private List work' });
    expect(screen.queryByRole('button', { name: /^Add list$/ })).not.toBeInTheDocument();
    expect(fetcher.mock.calls).toHaveLength(1);
  });
});
