import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationMembersPage } from './OrganizationMembersPage';
const org = '10000000-0000-0000-0000-000000000000';
const actor = '20000000-0000-0000-0000-000000000000';
const target = '30000000-0000-0000-0000-000000000000';
const row = { membershipId: '40000000-0000-0000-0000-000000000000', userId: target, displayName: 'Council member',
  email: 'member@example.test', role: 2, accountStatus: 'ACTIVE', emailVerified: true, isUsableOwner: false,
  joinedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', version: 1 };
const page = { organizationId: org, items: [row], nextCursor: null, actorRole: 0 };
const review = { organizationId: org, member: row, actorRole: 0 };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
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
  const mock = vi.fn().mockResolvedValueOnce(reply({ id: actor })).mockResolvedValueOnce(reply(page));
  for (const response of responses) { if (response instanceof Error) mock.mockRejectedValueOnce(response); else mock.mockResolvedValueOnce(response); }
  vi.stubGlobal('fetch', mock); return mock;
}
const confirm = () => screen.getByRole('button', { name: 'Confirm member removal' });
async function open() { fireEvent.click(await screen.findByRole('button', { name: 'Review removal of Council member' })); await screen.findByRole('dialog'); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('Organization member administration and renewed removal consent', () => {
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
    const mock = vi.fn().mockResolvedValueOnce(reply({ id: actor })).mockResolvedValueOnce(reply({}, 404)); vi.stubGlobal('fetch', mock); mount();
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
    const router = mount(); await open(); fireEvent.click(confirm()); await act(async () => { await router.navigate('/elsewhere'); });
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
    const mock = vi.fn().mockResolvedValueOnce(reply({ id: actor })).mockResolvedValueOnce(reply({ ...page, items: rows, nextCursor: rows[49].userId }))
      .mockResolvedValueOnce(reply({ ...page, items: [{ ...row, userId: '30000000-0000-0000-0000-000000000050' }] }))
      .mockResolvedValueOnce(reply({ ...page, items: [{ ...row, version: 'bad' }] })); vi.stubGlobal('fetch', mock); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Next members' })); await screen.findByText('Page 2. Membership may change while you browse.');
    expect(mock.mock.calls[2][0]).toContain(`?after=${rows[49].userId}`);
    fireEvent.click(screen.getByRole('button', { name: 'Previous members' })); await screen.findByText(/Unable to load current members/);
    expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
});
