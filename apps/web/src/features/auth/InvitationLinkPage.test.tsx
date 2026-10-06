import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationLinkPage } from './InvitationLinkPage';

const token = 'private_invitation_proof_12345678901234567890';
const item = { id: '11111111-1111-4111-8111-111111111111', organizationId: '22222222-2222-4222-8222-222222222222', organizationName: 'Invited council', surface: 'PORTAL', targetRole: 'OWNER', expiresAt: '2035-01-01T00:00:00Z' };
const ack = { invitationId: item.id, organizationId: item.organizationId, surface: item.surface, targetRole: item.targetRole };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const actor = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const other = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
function stableFetch(fetcher: typeof fetch) {
  vi.stubGlobal('fetch', (...args: Parameters<typeof fetch>) => String(args[0]) === '/me'
    ? Promise.resolve(reply({ id: actor })) : fetcher(...args));
}
function mount(entry = `/invitation#token=${token}`) {
  const router = createMemoryRouter([{ path: '/invitation', element: <InvitationLinkPage /> }, { path: '/elsewhere', element: <h1>Elsewhere</h1> }], { initialEntries: [entry] });
  return { ...render(<RouterProvider router={router} />), router };
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
describe('Invitation link review and acknowledgment', () => {
  it('scrubs the URL before requests, retains proof only in memory and requires separate review and acceptance', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockResolvedValueOnce(reply(ack)); stableFetch(fetch);
    const { router } = mount();
    await waitFor(() => expect(router.state.location.hash).toBe(''));
    expect(fetch).not.toHaveBeenCalled(); expect(document.body.textContent).not.toContain(token);
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: item.organizationName });
    expect(fetch.mock.calls[0][0]).toBe(`/invitations/review?expectedActorId=${actor}`);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ token });
    expect(screen.queryByText(/acceptance acknowledged/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Accept reviewed invitation' }));
    expect(await screen.findByRole('link', { name: 'Open Owner Portal' })).toHaveAttribute('href', `/portal/${item.organizationId}`);
    expect(fetch.mock.calls[1][0]).toBe(`/me/invitations/${item.id}/accept?expectedActorId=${actor}`);
    expect(fetch.mock.calls[1][1].body).toBe('{}');
    expect(sessionStorage.length).toBe(0); expect(localStorage.length).toBe(0);
  });
  it('recovers a lost acceptance by the same ID without submitting the bearer again', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockRejectedValueOnce(new Error('Lost acknowledgment')).mockResolvedValueOnce(reply(ack));
    stableFetch(fetch); mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Retry invitation acceptance' }));
    await screen.findByRole('link', { name: 'Open Owner Portal' });
    expect(fetch.mock.calls[2][0]).toBe(fetch.mock.calls[1][0]); expect(fetch.mock.calls[2][1].body).toBe('{}');
  });
  it('keeps the scrubbed proof while signing in and requires fresh review afterward', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({}, 401)).mockResolvedValueOnce(reply({ user: { id: item.id, email: 'invited@example.test' }, sessionExpiresAt: '2035-01-01T00:00:00Z' }))
      .mockResolvedValueOnce(reply(item)); stableFetch(fetch);
    const { router } = mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: 'StrataAI2' });
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'invited@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'StrataAI2' })).not.toBeInTheDocument());
    expect(router.state.location.pathname).toBe('/invitation'); expect(fetch).toHaveBeenCalledTimes(2);
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: item.organizationName });
    expect(fetch.mock.calls[2][1].body).toBe(fetch.mock.calls[0][1].body);
  });
  it('hides protected metadata after a lost session and recovers the original ID after fresh sign-in', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockResolvedValueOnce(reply({}, 401))
      .mockResolvedValueOnce(reply({ user: { id: item.id, email: 'invited@example.test' }, sessionExpiresAt: '2035-01-01T00:00:00Z' }))
      .mockResolvedValueOnce(reply(ack)); stableFetch(fetch); mount();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByRole('heading', { name: 'StrataAI2' });
    expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'invited@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled());
    expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
    await screen.findByRole('link', { name: 'Open Owner Portal' });
    expect(fetch.mock.calls[3][0]).toBe(fetch.mock.calls[1][0]);
  });
  it.each([400, 403, 404, 409])('clears protected preview and prevents writes after admission status %s', async status => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockResolvedValueOnce(reply({}, status)); stableFetch(fetch); mount();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' })); fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByText(/This link is unavailable/);
    expect(screen.queryByRole('heading', { name: item.organizationName })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Accept|Retry/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  });
  it.each([`/invitation?token=${token}`, `/invitation#token=${token}&token=${token}`, '/invitation#token=short'])('rejects malformed/query proof and scrubs %s', async entry => {
    const fetch = vi.fn(); stableFetch(fetch); const { router } = mount(entry);
    await waitFor(() => expect(router.state.location.hash + router.state.location.search).toBe(''));
    expect(screen.queryByRole('button', { name: 'Review invitation' })).not.toBeInTheDocument(); expect(fetch).not.toHaveBeenCalled();
  });
  it('does not accept malformed preview or acknowledge mismatched acceptance', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({ ...item, surface: 'GLOBAL' })).mockResolvedValueOnce(reply(item))
      .mockResolvedValueOnce(reply({ ...ack, invitationId: item.organizationId })); stableFetch(fetch); mount();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' })); await screen.findByText(/could not be reviewed/);
    expect(screen.queryByRole('button', { name: 'Accept reviewed invitation' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' })); fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByRole('button', { name: 'Retry invitation acceptance' });
    expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  });
  it('fences duplicate requests and times out a transport that ignores cancellation', async () => {
    const fetch = vi.fn().mockImplementation(() => new Promise(() => {})); stableFetch(fetch); mount();
    vi.useFakeTimers(); const button = screen.getByRole('button', { name: 'Review invitation' });
    fireEvent.click(button); fireEvent.click(button);
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(fetch).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000); });
    expect(screen.getByText(/could not be reviewed/)).toBeInTheDocument(); expect(button).toBeEnabled();
  });
  it('ignores late review results after navigation', async () => {
    let resolve!: (value: Response) => void;
    const fetch = vi.fn().mockImplementation(() => new Promise<Response>(done => { resolve = done; })); stableFetch(fetch);
    const { router } = mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await waitFor(() => expect(resolve).toBeDefined());
    await act(async () => { await router.navigate('/elsewhere'); resolve(reply(item)); });
    expect(screen.getByRole('heading', { name: 'Elsewhere' })).toBeInTheDocument(); expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
  });
});

const boardItem = { ...item, surface: 'INTERNAL', targetRole: 'MEMBER', boardName: 'Private maintenance Board',
  boardTarget: { boardId: '33333333-3333-4333-8333-333333333333', role: 'ADMIN' } };
const boardAck = { ...ack, surface: 'INTERNAL', targetRole: 'MEMBER', boardTarget: boardItem.boardTarget };
it('shows the Board role and recovers a lost acknowledgment for the exact reviewed Board', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(boardItem)).mockRejectedValueOnce(new Error('Lost acknowledgment')).mockResolvedValueOnce(reply(boardAck));
  stableFetch(fetch); mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
  await screen.findByRole('heading', { name: boardItem.boardName });
  expect(screen.getByText('Board access · admin')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Accept reviewed invitation' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Retry invitation acceptance' }));
  expect(await screen.findByRole('link', { name: 'Open Board' })).toHaveAttribute('href', `/app/${item.organizationId}/boards/${boardItem.boardTarget.boardId}`);
  expect(fetch.mock.calls[2][0]).toBe(fetch.mock.calls[1][0]); expect(fetch.mock.calls[2][1].body).toBe('{}');
});
it.each([
  { ...boardAck, boardTarget: null },
  { ...boardAck, boardTarget: { ...boardItem.boardTarget, boardId: item.id } },
  { ...boardAck, boardTarget: { ...boardItem.boardTarget, role: 'MEMBER' } },
])('refuses acceptance acknowledgment for a different Board target: %j', async response => {
  const fetch = vi.fn().mockResolvedValueOnce(reply(boardItem)).mockResolvedValueOnce(reply(response)); stableFetch(fetch); mount();
  fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
  await screen.findByRole('button', { name: 'Retry invitation acceptance' });
  expect(screen.queryByRole('link', { name: 'Open Board' })).not.toBeInTheDocument();
});
it.each([
  { ...boardItem, surface: 'PORTAL', targetRole: 'OWNER' },
  { ...boardItem, boardName: null },
  { ...boardItem, boardTarget: { ...boardItem.boardTarget, role: 'OWNER' } },
  { ...boardItem, boardTarget: { ...boardItem.boardTarget, boardId: '00000000-0000-0000-0000-000000000000' } },
  { ...item, boardName: boardItem.boardName },
])('rejects invalid or unsupported Board preview before disclosure: %j', async response => {
  stableFetch(vi.fn().mockResolvedValue(reply(response))); mount();
  fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
  await screen.findByText('The invitation could not be reviewed. Wait and retry.');
  expect(screen.queryByText(boardItem.boardName)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Accept reviewed invitation' })).not.toBeInTheDocument();
});

const organizationItem = { ...item, surface: 'INTERNAL', targetRole: 'MEMBER' };
it.each([item, organizationItem, boardItem].flatMap(row => ['before', 'after'].map(phase => ({ row, phase }))))(
  'withdraws $row.surface proof consent when the account changes $phase acceptance', async ({ row, phase }) => {
    let profiles = 0; const commands: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: string) => {
      if (input === '/me') return reply({ id: ++profiles === (phase === 'before' ? 3 : 4) ? other : actor });
      expect(new URL(input, 'https://test').searchParams.get('expectedActorId')).toBe(actor);
      commands.push(input);
      return input.startsWith('/invitations/review') ? reply(row)
        : reply({ invitationId: row.id, organizationId: row.organizationId, surface: row.surface, targetRole: row.targetRole,
          boardTarget: 'boardTarget' in row ? row.boardTarget : undefined });
    }));
    mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByRole('heading', { name: 'StrataAI2' });
    expect(commands.filter(path => path.includes('/accept?'))).toHaveLength(phase === 'before' ? 0 : 1);
    expect(screen.queryByText(row.organizationName)).not.toBeInTheDocument();
    expect(screen.queryByText('Private maintenance Board')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /^Open / })).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain(token);
  });
it('withholds preview and fences consent when the account changes during the proof read', async () => {
  let profiles = 0;
  vi.stubGlobal('fetch', vi.fn(async (input: string) => input === '/me' ? reply({ id: ++profiles === 1 ? actor : other }) : reply(boardItem)));
  mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
  await screen.findByRole('heading', { name: 'StrataAI2' });
  expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument(); expect(screen.queryByText(boardItem.boardName)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Accept reviewed invitation' })).not.toBeInTheDocument();
});
it.each([item, organizationItem, boardItem])('withholds a committed $surface acknowledgment after temporary profile failure and retries the same ID', async row => {
  let profiles = 0; const writes: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: string) => {
    if (input === '/me') return ++profiles === 4 ? reply({}, 503) : reply({ id: actor });
    if (input.startsWith('/invitations/review')) return reply(row);
    writes.push(input);
    return reply({ invitationId: row.id, organizationId: row.organizationId, surface: row.surface, targetRole: row.targetRole,
      boardTarget: 'boardTarget' in row ? row.boardTarget : undefined });
  }));
  mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
  await screen.findByText('The reviewed account could not be confirmed. Retry after account access is available.');
  expect(screen.queryByText(row.organizationName)).not.toBeInTheDocument(); expect(screen.queryByText(boardItem.boardName)).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /^Open / })).not.toBeInTheDocument(); expect(writes).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
  await screen.findByRole('link', { name: /^Open / });
  expect(writes).toHaveLength(2); expect(writes[1]).toBe(writes[0]);
  expect(sessionStorage.length).toBe(0); expect(localStorage.length).toBe(0);
});
it.each(['before', 'after'])('bounds a non-cooperating account read %s acceptance without restoring late consent', async phase => {
  let profiles = 0; let late: ((value: Response) => void) | undefined; let signal: AbortSignal | undefined;
  const writes: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit) => {
    if (input === '/me') {
      if (++profiles === (phase === 'before' ? 3 : 4)) {
        signal = init.signal as AbortSignal;
        return await new Promise<Response>(resolve => { late = resolve; });
      }
      return reply({ id: actor });
    }
    if (input.startsWith('/invitations/review')) return reply(item);
    writes.push(input); return reply(ack);
  }));
  mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
  const button = await screen.findByRole('button', { name: 'Accept reviewed invitation' });
  vi.useFakeTimers(); fireEvent.click(button);
  await act(async () => { await vi.advanceTimersByTimeAsync(16_000); });
  expect(signal?.aborted).toBe(true); expect(writes).toHaveLength(phase === 'before' ? 0 : 1);
  expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  vi.useRealTimers(); await act(async () => { late!(reply({ id: actor })); });
  expect(writes).toHaveLength(phase === 'before' ? 0 : 1); expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
});
