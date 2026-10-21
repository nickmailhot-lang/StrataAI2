import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardScreen } from './BoardScreen';
import { AppThemeFixture } from '../../test/AppThemeFixture';
import { watchBoard } from '../../api/boardLive';
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const actor = '11111111-1111-1111-1111-111111111111';
const profile = { id: actor, status: 'ACTIVE', version: 1, locale: 'en', timezone: 'UTC', emailVerified: true };
const snapshot = { board: { id: 'board', organizationId: 'org', name: 'Private work', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: [{ list: { id: 'list', name: 'Planning', rank: 'a', lifecycleState: 'active' }, cards: [] }] };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount(outsideAction = false) {
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> }, { path: '/elsewhere', element: <p>Elsewhere</p> }], { initialEntries: ['/app/org/boards/board'] });
  render(<>{outsideAction && <button>Independent navigation</button>}<RouterProvider router={router} /></>, { wrapper: AppThemeFixture }); return router;
}
afterEach(() => { vi.unstubAllGlobals(); vi.mocked(watchBoard).mockReset(); });
describe('PRD-08-VS-04 Card creation identity', () => {
  it('keeps an unconfirmed form unavailable and opens it only after bounded authenticated admission', async () => {
    let finish!: (value: Response) => void;
    const fetcher = vi.fn(async (path: string) => path === '/me' ? new Promise<Response>(resolve => { finish = resolve; }) : reply(snapshot));
    vi.stubGlobal('fetch', fetcher); mount();
    const launcher = await screen.findByRole('button', { name: 'Add card to Planning' });
    fireEvent.click(launcher);
    await waitFor(() => expect(finish).toBeDefined());
    expect(launcher).toBeDisabled(); expect(screen.queryByRole('dialog', { name: 'Add card' })).not.toBeInTheDocument();
    await act(async () => finish(reply(profile)));
    expect(await screen.findByLabelText(/Card title/)).toBeVisible();
    expect(fetcher.mock.calls.filter(([path]) => path === '/me')).toHaveLength(1);
  });

  it('retains original actor, key and body through explicit retry and withdraws on server account refusal', async () => {
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
    fireEvent.click(await screen.findByRole('button', { name: 'Add card to Planning' }));
    fireEvent.change(await screen.findByLabelText(/Card title/), { target: { value: 'Original Card draft' } });
    const form = screen.getByRole('dialog', { name: 'Add card' });
    fireEvent.click(within(form).getByRole('button', { name: /^Create$/ }));
    expect(await within(form).findByRole('alert')).toHaveTextContent('Unable to confirm the save');
    fireEvent.click(within(form).getByRole('button', { name: /^Create$/ }));
    await waitFor(() => expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument());
    expect(screen.queryByText('Private work')).not.toBeInTheDocument();
    expect(writes).toHaveLength(2); expect(writes[0].path).toBe(`/lists/list/cards?expectedActorId=${actor}`);
    expect(writes[1]).toEqual(writes[0]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
    expect(fetcher.mock.calls.filter(([path]) => path === '/me')).toHaveLength(1);
    await waitFor(() => expect(screen.getByRole('alert')).toHaveFocus());
  });

  it.each(['unavailable', 'invalid'] as const)('allows explicit entry retry after %s identity without exposing a form or writing', async scenario => {
    let reads = 0;
    const fetcher = vi.fn(async (path: string) => path === '/me'
      ? ++reads === 1 ? reply(scenario === 'invalid' ? { ...profile, id: 'not-an-actor' } : {}, scenario === 'invalid' ? 200 : 503) : reply(profile)
      : reply(snapshot));
    vi.stubGlobal('fetch', fetcher); mount();
    const launcher = await screen.findByRole('button', { name: 'Add card to Planning' }); fireEvent.click(launcher);
    expect(await screen.findByRole('alert')).toHaveTextContent('Service temporarily unavailable');
    expect(screen.queryByRole('dialog', { name: 'Add card' })).not.toBeInTheDocument();
    await waitFor(() => expect(launcher).toBeEnabled()); fireEvent.click(launcher);
    expect(await screen.findByLabelText(/Card title/)).toBeVisible(); expect(reads).toBe(2);
  });

  it('cannot open a late admitted form after a newer protected Board refusal', async () => {
    let invalidate!: () => void, finish!: (value: Response) => void, boardReads = 0;
    vi.mocked(watchBoard).mockImplementation(options => { invalidate = options.invalidate; return () => {}; });
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
      ? new Promise<Response>(resolve => { finish = resolve; }) : ++boardReads === 1 ? reply(snapshot) : reply({}, 403)));
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Add card to Planning' }));
    await waitFor(() => expect(finish).toBeDefined()); act(() => invalidate());
    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable');
    await act(async () => finish(reply(profile)));
    expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument(); expect(screen.queryByText('Private work')).not.toBeInTheDocument();
  });

  it('cancels a pending creation admission on route departure', async () => {
    let signal: AbortSignal | null | undefined;
    vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') { signal = options?.signal; return new Promise<Response>(() => {}); }
      return reply(snapshot);
    }));
    const router = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Add card to Planning' }));
    await waitFor(() => expect(signal).toBeDefined()); await act(async () => { await router.navigate('/elsewhere'); });
    expect(signal?.aborted).toBe(true); expect(screen.getByText('Elsewhere')).toBeVisible();
  });

  it('preserves deliberate independent navigation focus when a pending Card admission is refused', async () => {
    let finish!: (value: Response) => void;
    vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me'
      ? new Promise<Response>(resolve => { finish = resolve; }) : reply(snapshot)));
    mount(true); fireEvent.click(await screen.findByRole('button', { name: 'Add card to Planning' }));
    await waitFor(() => expect(finish).toBeDefined());
    const navigation = screen.getByRole('button', { name: 'Independent navigation' }); navigation.focus();
    await act(async () => finish(reply({}, 401)));
    await waitFor(() => expect(screen.queryByText('Private work')).not.toBeInTheDocument());
    expect(screen.getByRole('alert')).toHaveTextContent('Sign in to continue');
    expect(navigation).toHaveFocus(); expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument();
  });
});
