import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationMembersPage } from './OrganizationMembersPage';
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { organizationId: string; userId: string;
  invalidate(): void; reset(): void; unavailable(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const org = '10000000-0000-0000-0000-000000000000';
const actor = '20000000-0000-0000-0000-000000000000';
const target = '30000000-0000-0000-0000-000000000000';
const row = { membershipId: '40000000-0000-0000-0000-000000000000', userId: target, displayName: 'Council member',
  email: 'member@example.test', role: 2, accountStatus: 'ACTIVE', emailVerified: true, isUsableOwner: false,
  joinedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', version: 1 };
const page = { organizationId: org, items: [row], nextCursor: null, actorRole: 0 };
const review = { organizationId: org, member: row, actorRole: 0 };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
let currentActor = actor;
function stubFetch(delegate: (path: string, options?: RequestInit) => unknown) {
  vi.stubGlobal('fetch', (path: string, options?: RequestInit) => path === '/me'
    ? Promise.resolve(reply({ id: currentActor })) : delegate(path, options));
}
beforeEach(() => { currentActor = actor; vi.clearAllMocks(); });
function mount() {
  const router = createMemoryRouter([
    { path: '/app/:organizationId/members', element: <OrganizationMembersPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
    { path: '/elsewhere', element: <h1>Other destination</h1> },
    { path: '/app', element: <h1>Organizations destination</h1> },
  ], { initialEntries: [`/app/${org}/members`] });
  render(<RouterProvider router={router} />); return router;
}
function fetcher(...responses: (Response | Error)[]) {
  const mock = vi.fn().mockResolvedValueOnce(reply(page));
  for (const response of responses) { if (response instanceof Error) mock.mockRejectedValueOnce(response); else mock.mockResolvedValueOnce(response); }
  stubFetch(mock); return mock;
}
const confirm = () => screen.getByRole('button', { name: 'Confirm member removal' });
async function open() { fireEvent.click(await screen.findByRole('button', { name: 'Review removal of Council member' })); await screen.findByRole('dialog'); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('Organization member administration and renewed removal consent', () => {
  it('retires live stale confirmation and reviews the newly read version before any removal', async () => {
    const changed = { ...row, displayName: 'Updated council member', version: 2 };
    const mock = fetcher(reply(review), reply({ ...page, items: [changed] }), reply({ ...review, member: changed }), reply(undefined, 204));
    mount(); await open();
    act(() => live.watch.mock.calls.at(-1)![0].invalidate());
    await screen.findByRole('button', { name: 'Review removal of Updated council member' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Review removal of Updated council member' }));
    await screen.findByRole('dialog'); fireEvent.click(confirm()); await screen.findByText('Member removed.');
    expect(mock.mock.calls.find(call => call[1]?.method === 'DELETE')![0]).toContain('expectedVersion=2');
  });
  it('checks live access while preserving an uncertain original removal without showing a new consent', async () => {
    const mock = fetcher(reply(review), new Error('lost'), reply({ ...page, items: [{ ...row, version: 3 }] }), reply(undefined, 204));
    mount(); await open(); fireEvent.click(confirm());
    await screen.findByRole('button', { name: 'Retry original removal' });
    act(() => live.watch.mock.calls.at(-1)![0].reset());
    await screen.findByText('Current access checked. Retry the original removal before reviewing later membership.');
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Retry original removal' }));
    await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
    const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE');
    expect(writes).toHaveLength(2); expect(writes[1][0]).toBe(writes[0][0]);
    expect(writes[1][1].headers.get('Idempotency-Key')).toBe(writes[0][1].headers.get('Idempotency-Key'));
  });
  it('clears original recovery on confirmed live administrative access withdrawal', async () => {
    const stop = vi.fn(); live.watch.mockReturnValueOnce(stop);
    const mock = fetcher(reply(review), new Error('lost'), reply({}, 404));
    mount(); await open(); fireEvent.click(confirm()); await screen.findByRole('button', { name: 'Retry original removal' });
    act(() => live.watch.mock.calls.at(-1)![0].unavailable());
    await screen.findByText('Organization members are unavailable to your account.');
    expect(screen.queryByRole('button', { name: 'Retry original removal' })).not.toBeInTheDocument();
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
    await waitFor(() => expect(stop).toHaveBeenCalledOnce());
  });
  it('waits for an in-flight removal before live reconciliation and preserves its original intent on uncertainty', async () => {
    let resolve!: (response: Response) => void;
    const mock = fetcher(reply(review));
    mock.mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }));
    mock.mockResolvedValueOnce(reply({ ...page, items: [{ ...row, version: 3 }] }));
    mock.mockResolvedValueOnce(reply(undefined, 204));
    mount(); await open(); fireEvent.click(confirm());
    await waitFor(() => expect(resolve).toBeTypeOf('function'));
    act(() => live.watch.mock.calls.at(-1)![0].invalidate());
    expect(mock.mock.calls).toHaveLength(3);
    await act(async () => resolve(reply({}, 503)));
    await screen.findByText('Current access checked. Retry the original removal before reviewing later membership.');
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original removal' }));
    await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
    const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE');
    expect(writes).toHaveLength(2); expect(writes[1][0]).toBe(writes[0][0]);
    expect(writes[1][1].headers.get('Idempotency-Key')).toBe(writes[0][1].headers.get('Idempotency-Key'));
  });
  it('refuses removal when the current account replaced the account that reviewed membership', async () => {
    const mock = fetcher(reply(review)); mount(); await open(); currentActor = target;
    fireEvent.click(confirm()); await screen.findByText('Sign in destination');
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('discards a live response when account replacement occurs during its read', async () => {
    const mock = fetcher(); mount(); await screen.findByRole('button', { name: 'Review removal of Council member' });
    mock.mockImplementationOnce(() => { currentActor = target; return Promise.resolve(reply({ ...page, items: [{ ...row, displayName: 'Other account private member' }] })); });
    act(() => live.watch.mock.calls.at(-1)![0].invalidate());
    await screen.findByText('Sign in destination');
    expect(screen.queryByText('Other account private member')).not.toBeInTheDocument();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  });
  it('does not restore stale consent from a review that completed after a live invalidation', async () => {
    let resolve!: (response: Response) => void;
    const mock = fetcher(); mock.mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }));
    mock.mockResolvedValueOnce(reply(page)); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Review removal of Council member' }));
    await waitFor(() => expect(resolve).toBeTypeOf('function'));
    act(() => live.watch.mock.calls.at(-1)![0].invalidate());
    await act(async () => resolve(reply(review)));
    await screen.findByText('Current members checked. Review a membership again before confirming removal.');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  });
  it('reviews current membership, focuses safe cancellation and sends the reviewed version once', async () => {
    const mock = fetcher(reply({ ...review, member: { ...row, role: 1, version: 7 } }), reply(undefined, 204)); mount(); await open();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel removal' })).toHaveFocus());
    expect(screen.getByText('Current role: Admin')).toBeInTheDocument();
    fireEvent.click(confirm()); fireEvent.click(confirm()); await screen.findByText('Member removed.');
    const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(writes).toHaveLength(1);
    expect(writes[0][0]).toBe(`/organizations/${org}/members/${target}?expectedVersion=7&expectedActorId=${actor}`);
    expect(writes[0][1].headers.get('X-StrataAI-Request')).toBe('1'); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('requires a fresh review after role/version conflict before confirming again', async () => {
    const mock = fetcher(reply(review), reply({ code: 'member_version_conflict' }, 409),
      reply({ ...review, member: { ...row, role: 1, version: 2 } }), reply(undefined, 204)); mount(); await open(); fireEvent.click(confirm());
    await screen.findByText(/The membership changed elsewhere/); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Review current membership' })); await screen.findByText('Current role: Admin');
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
    fireEvent.click(confirm()); await screen.findByText('Member removed.'); expect(mock.mock.calls.at(-1)?.[0]).toContain('expectedVersion=2');
  });
  it('replays the original removal after a lost acknowledgment without removing a later rejoin', async () => {
    const mock = fetcher(reply(review), new Error('response lost'), reply(undefined, 204),
      reply({ ...review, member: { ...row, version: 3 } })); mount(); await open(); fireEvent.click(confirm());
    await screen.findByText(/The removal could not be confirmed/); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Load current members' })).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Review current membership' })).toBeDisabled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const retry = await screen.findByRole('button', { name: 'Retry original removal' }); retry.focus(); fireEvent.click(retry);
    await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
    expect(screen.queryByText('Member removed.')).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('status')).toHaveFocus());
    const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE');
    expect(writes).toHaveLength(2); expect(writes[1][0]).toBe(writes[0][0]);
    expect(writes[1][1].headers.get('Idempotency-Key')).toBe(writes[0][1].headers.get('Idempotency-Key'));
    expect(writes[0][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    fireEvent.click(await screen.findByRole('button', { name: 'Review current membership' }));
    await screen.findByRole('dialog'); expect(mock.mock.calls.at(-1)?.[0]).toBe(`/organizations/${org}/members/${target}`);
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(2);
  });
  it('reports current absence separately after the original removal is acknowledged', async () => {
    fetcher(reply(review), new Error('lost'), reply(undefined, 204), reply({ ...review, member: null }));
    mount(); await open(); fireEvent.click(confirm());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original removal' }));
    await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
    fireEvent.click(await screen.findByRole('button', { name: 'Review current membership' }));
    await screen.findByText('This person is currently no longer an internal member.');
    expect(screen.queryByText(/earlier removal acknowledgment was unavailable/)).not.toBeInTheDocument();
    expect(screen.queryByText('Member removed.')).not.toBeInTheDocument();
  });
  it('retains original removal intent through repeated uncertainty and clears it on expiry refusal', async () => {
    const mock = fetcher(reply(review), new Error('lost'), reply({}, 503), reply({ code: 'idempotency_expired' }, 409));
    mount(); await open(); fireEvent.click(confirm());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original removal' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original removal' })).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Review current membership' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original removal' }));
    await screen.findByText('The removal was refused. Review current membership before considering another removal.');
    expect(screen.queryByRole('button', { name: 'Retry original removal' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review current membership' })).toBeEnabled();
    const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE');
    expect(writes).toHaveLength(3); expect(new Set(writes.map(call => call[0])).size).toBe(1);
    expect(new Set(writes.map(call => call[1].headers.get('Idempotency-Key'))).size).toBe(1);
  });
  it.each([401, 403, 404])('clears original removal recovery and private state after access refusal (%s)', async status => {
    fetcher(reply(review), new Error('lost'), reply({}, status)); mount(); await open(); fireEvent.click(confirm());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original removal' }));
    await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization members are unavailable to your account.');
    expect(screen.queryByRole('button', { name: 'Retry original removal' })).not.toBeInTheDocument();
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('reconciles a definitive missing-member refusal without claiming removal or blocking administrator review', async () => {
    const mock = fetcher(reply(review), reply({ code: 'member_not_found' }, 404), reply({ ...review, member: null }));
    mount(); await open(); fireEvent.click(confirm());
    await screen.findByText('The removal was refused. Review current membership before considering another removal.');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Retry original removal' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review current membership' }));
    await screen.findByText(/currently no longer an internal member/);
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
    expect(screen.queryByText('Member removed.')).not.toBeInTheDocument();
  });
  it('keeps sole-owner denial actionable and requires review before another request', async () => {
    fetcher(reply({ ...review, member: { ...row, role: 0, isUsableOwner: true } }), reply({ code: 'sole_owner' }, 409));
    mount(); await open(); fireEvent.click(confirm()); await screen.findByText(/needs another usable Owner/);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(screen.getByRole('button', { name: 'Review current membership' })).toBeEnabled();
  });
  it.each([401, 403, 404])('clears private directory after current-member review loses access (%s)', async status => {
    fetcher(reply({}, status)); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Review removal of Council member' }));
    await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization members are unavailable to your account.');
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it('denies direct member/Portal access without rendering directory data', async () => {
    const mock = vi.fn().mockResolvedValueOnce(reply({}, 404)); stubFetch(mock); mount();
    await screen.findByText('Organization members are unavailable to your account.'); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('uses the fresh actor role to prevent an Admin from confirming removal of an Owner', async () => {
    const mock = fetcher(reply({ ...review, actorRole: 1, member: { ...row, role: 0, isUsableOwner: true } })); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Review removal of Council member' })); await screen.findByText(/Only an Owner can remove an Owner/);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  });
  it('does not accept malformed or cross-Organization review data', async () => {
    fetcher(reply({ ...review, organizationId: actor })); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Review removal of Council member' })); await screen.findByText(/Unable to review this membership/);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('fences a delayed removal response after navigation', async () => {
    let resolve!: (response: Response) => void;
    const mock = fetcher(reply(review)); mock.mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }));
    const router = mount(); await open(); fireEvent.click(confirm());
    await waitFor(() => expect(resolve).toBeTypeOf('function'));
    await act(async () => { await router.navigate('/elsewhere'); });
    await act(async () => resolve(reply(undefined, 204))); expect(screen.getByText('Other destination')).toBeInTheDocument(); expect(screen.queryByText('Member removed.')).not.toBeInTheDocument();
  });
  it('bounds an abort-ignoring removal and fences its late acknowledgment', async () => {
    let resolve!: (response: Response) => void;
    const mock = fetcher(reply(review)); mock.mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }));
    mount(); await open(); vi.useFakeTimers(); fireEvent.click(confirm());
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000); });
    expect(screen.getByText(/The removal could not be confirmed/)).toBeInTheDocument();
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
    await act(async () => resolve(reply(undefined, 204)));
    expect(screen.queryByText('Member removed.')).not.toBeInTheDocument(); expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  });
  it('paginates with server cursors and rejects malformed pages', async () => {
    const rows = Array.from({ length: 50 }, (_, index) => ({ ...row, membershipId: `40000000-0000-0000-0000-${String(index).padStart(12, '0')}`,
      userId: `30000000-0000-0000-0000-${String(index).padStart(12, '0')}` }));
    const mock = vi.fn().mockResolvedValueOnce(reply({ ...page, items: rows, nextCursor: rows[49].userId }))
      .mockResolvedValueOnce(reply({ ...page, items: [{ ...row, userId: '30000000-0000-0000-0000-000000000050' }] }))
      .mockResolvedValueOnce(reply({ ...page, items: [{ ...row, version: 'bad' }] })); stubFetch(mock); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Next members' })); await screen.findByText('Page 2. Membership may change while you browse.');
    expect(mock.mock.calls[1][0]).toContain(`?after=${rows[49].userId}`);
    fireEvent.click(screen.getByRole('button', { name: 'Previous members' })); await screen.findByText(/Unable to load current members/);
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
});

describe('PRD-03 complete member-operation deadlines', () => {
  it.each(['load', 'review'] as const)('bounds the complete %s and suppresses late private JSON', async operation => {
    let timed = operation === 'load'; let delayed = false; let finishBody!: (value: unknown) => void; let signal!: AbortSignal;
    const mock = vi.fn((path: string, options: RequestInit = {}) => {
      if (path === '/me') {
        if (timed && !delayed) { delayed = true; return new Promise<Response>(resolve => setTimeout(() => resolve(reply({ id: actor })), 8000)); }
        return Promise.resolve(reply({ id: actor }));
      }
      if (timed) {
        signal = options.signal!; const result = reply(operation === 'load' ? page : review);
        result.json = () => new Promise<unknown>(resolve => { finishBody = resolve; }); return Promise.resolve(result);
      }
      return Promise.resolve(reply(path.endsWith(target) ? review : page));
    }); vi.stubGlobal('fetch', mock);
    if (operation === 'review') { mount(); await screen.findByRole('button', { name: 'Review removal of Council member' }); timed = true; vi.useFakeTimers();
      await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Review removal of Council member' })));
    } else { vi.useFakeTimers(); await act(async () => mount()); }
    await act(async () => vi.advanceTimersByTimeAsync(8000)); expect(finishBody).toBeDefined();
    await act(async () => vi.advanceTimersByTimeAsync(7001)); expect(signal.aborted).toBe(true);
    expect(screen.getByText(operation === 'load' ? 'Unable to load current members. Please retry.'
      : 'Unable to review this membership. No further removal will be sent until the current membership is reviewed.')).toBeInTheDocument();
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await act(async () => finishBody(operation === 'load' ? page : review));
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    timed = false; vi.useRealTimers();
    fireEvent.click(screen.getByRole('button', { name: operation === 'load' ? 'Load current members' : 'Review current membership' }));
    if (operation === 'load') await screen.findByRole('button', { name: 'Review removal of Council member' }); else await screen.findByRole('dialog');
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  });
  it('bounds removal through final account JSON and recovers its identical original command', async () => {
    let timed = false; let checks = 0; let finishBody!: (value: unknown) => void; let signal!: AbortSignal;
    const mock = vi.fn((path: string, options: RequestInit = {}) => {
      if (path === '/me') {
        if (timed && ++checks === 1) return new Promise<Response>(resolve => setTimeout(() => resolve(reply({ id: actor })), 8000));
        if (timed && checks === 2) { signal = options.signal!; const final = reply({ id: actor });
          final.json = () => new Promise<unknown>(resolve => { finishBody = resolve; }); return Promise.resolve(final); }
        return Promise.resolve(reply({ id: actor }));
      }
      return Promise.resolve(options.method === 'DELETE' ? reply(undefined, 204) : reply(path.endsWith(target) ? review : page));
    }); vi.stubGlobal('fetch', mock); mount(); await open(); timed = true; vi.useFakeTimers();
    await act(async () => fireEvent.click(confirm())); await act(async () => vi.advanceTimersByTimeAsync(8000)); expect(finishBody).toBeDefined();
    await act(async () => vi.advanceTimersByTimeAsync(7001)); expect(signal.aborted).toBe(true);
    await act(async () => vi.advanceTimersByTimeAsync(500)); // Finish the ordinary MUI dialog exit.
    expect(screen.getByRole('button', { name: 'Retry original removal' })).toBeEnabled();
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Member removed.')).not.toBeInTheDocument();
    await act(async () => finishBody({ id: actor })); expect(screen.queryByText('Member removed.')).not.toBeInTheDocument();
    timed = false; vi.useRealTimers(); fireEvent.click(screen.getByRole('button', { name: 'Retry original removal' }));
    await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
    const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(writes).toHaveLength(2);
    expect(writes[1][0]).toBe(writes[0][0]);
    expect(new Headers(writes[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(writes[0][1]?.headers).get('Idempotency-Key'));
  });
  it('reserves an unsent removal through preflight timeout and sends it only on explicit recovery', async () => {
    let timed = false; let finishBody!: (value: unknown) => void;
    const mock = vi.fn((path: string, options: RequestInit = {}) => {
      if (path === '/me') { const me = reply({ id: actor }); if (timed) me.json = () => new Promise<unknown>(resolve => { finishBody = resolve; }); return Promise.resolve(me); }
      return Promise.resolve(options.method === 'DELETE' ? reply(undefined, 204) : reply(path.endsWith(target) ? review : page));
    }); vi.stubGlobal('fetch', mock); mount(); await open(); timed = true; vi.useFakeTimers();
    await act(async () => fireEvent.click(confirm())); await act(async () => vi.advanceTimersByTimeAsync(15001));
    await act(async () => vi.advanceTimersByTimeAsync(500)); // Finish the ordinary MUI dialog exit.
    expect(screen.getByRole('button', { name: 'Retry original removal' })).toBeEnabled();
    await act(async () => finishBody({ id: actor })); expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
    timed = false; vi.useRealTimers(); fireEvent.click(screen.getByRole('button', { name: 'Retry original removal' }));
    await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  });
});
