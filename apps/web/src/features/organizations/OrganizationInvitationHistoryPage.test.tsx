import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationInvitationHistoryPage, BoardInvitationHistoryPage } from './OrganizationInvitationHistoryPage';
const live = vi.hoisted(() => ({ watch: vi.fn() }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const boardLive = vi.hoisted(() => ({ watch: vi.fn() }));
vi.mock('../../api/boardLive', () => ({ watchBoard: boardLive.watch }));

const org = '10000000-0000-0000-0000-000000000001';
const profile = { id: '40000000-0000-4000-8000-000000000004', locale: 'en-CA', timezone: 'Pacific/Honolulu' };
let currentProfile = profile;
const row = { id: '20000000-0000-0000-0000-000000000001', email: 'issued@example.test', surface: 'INTERNAL', targetRole: 'MEMBER',
  createdAt: '2034-01-01T00:00:00Z', expiresAt: '2035-01-08T18:00:00Z', acceptedAt: null as string | null, revokedAt: null as string | null, deliveryState: 'SENT' };
const bodies = new WeakMap<Response, unknown>();
const reply = (body: unknown, status = 200) => {
  const response = new Response(status === 204 ? null : JSON.stringify(body), { status }); bodies.set(response, body); return response;
};
const page = (items = [row]) => ({ items, nextCursor: null });
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/invitations', element: <OrganizationInvitationHistoryPage /> }],
    { initialEntries: [`/app/${org}/invitations`] });
  return render(<RouterProvider router={router} />);
}
function fetcher(...responses: (Response | Error)[]) {
  const queue = responses.filter(response => response instanceof Error || bodies.get(response) !== profile);
  const mock = vi.fn(async (path: string, _options?: RequestInit) => {
    if (path === '/me') return reply(currentProfile);
    const response = queue.shift(); if (response instanceof Error) throw response;
    if (!response) throw new Error(`Unexpected request: ${path}`); return response;
  }); vi.stubGlobal('fetch', mock); return mock;
}
async function review() { fireEvent.click(await screen.findByRole('button', { name: `Revoke invitation for ${row.email}` })); await screen.findByRole('dialog'); }
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
beforeEach(() => { currentProfile = profile; live.watch.mockReset(); live.watch.mockReturnValue(() => {});
  boardLive.watch.mockReset(); boardLive.watch.mockReturnValue(() => {}); });
async function invalidate() { await act(async () => { live.watch.mock.calls.at(-1)![0].invalidate(); }); }

it('retires reviewed revocation consent and reloads newly issued invitations after a live source', async () => {
  const later = { ...row, id: '20000000-0000-0000-0000-000000000002', email: 'later@example.test' };
  const mock = fetcher(reply(profile), reply(page()), reply(page([row, later])));
  mount(); await review(); await invalidate();
  await screen.findByRole('heading', { name: later.email });
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(live.watch.mock.calls[0][0]).toMatchObject({ organizationId: org, userId: profile.id });
  expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
});
it('preserves an unresolved original revocation when live recovery cannot find its current row', async () => {
  const mock = fetcher(reply(profile), reply(page()), new Error('lost acknowledgment'), reply(page([])));
  mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
  await screen.findByText(/Revocation could not be confirmed/); await invalidate();
  await screen.findByText('Revocation is still unconfirmed. Review the remaining invitation pages to locate its current state.');
  expect(await screen.findByRole('button', { name: 'Check revocation' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: 'Create invitation' })).not.toBeInTheDocument();
  expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
});
it('withdraws private rows and sends no revocation when the reviewed account is replaced', async () => {
  const mock = fetcher(reply(profile), reply(page())); mount(); await review();
  currentProfile = { ...profile, id: '50000000-0000-4000-8000-000000000005' };
  fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' })); await screen.findByText(/Sign in again/);
  expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
});

it('discards history when the account changes during the protected read', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return reply(currentProfile);
    currentProfile = { ...profile, id: '50000000-0000-4000-8000-000000000005' }; return reply(page());
  }));
  mount(); await screen.findByText(/Sign in again/);
  expect(screen.queryByRole('heading', { name: row.email })).not.toBeInTheDocument();
});
it('fences an obsolete in-flight history read after a newer live change', async () => {
  let resolve: ((response: Response) => void) | undefined; let reads = 0;
  const later = { ...row, id: '20000000-0000-0000-0000-000000000002', email: 'current@example.test' };
  const obsolete = { ...row, email: 'obsolete@example.test' };
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return reply(profile);
    reads++; if (reads === 1) return reply(page());
    if (reads === 2) return new Promise<Response>(done => { resolve = done; });
    return reply(page([later]));
  }));
  mount(); await screen.findByRole('heading', { name: row.email });
  await invalidate(); await waitFor(() => expect(resolve).toBeDefined()); await invalidate();
  await act(async () => { resolve!(reply(page([obsolete]))); });
  await screen.findByRole('heading', { name: later.email });
  expect(screen.queryByRole('heading', { name: obsolete.email })).not.toBeInTheDocument();
  expect(reads).toBe(3);
});

describe('PRD-60 administrator invitation history and revocation', () => {
  it('displays lifecycle separately from sending status using account time preferences', async () => {
    fetcher(reply(profile), reply(page())); mount(); await screen.findByText(row.email);
    expect(screen.getByText('Email sent')).toBeInTheDocument(); expect(screen.getByText('Awaiting acceptance')).toBeInTheDocument();
    expect(screen.getByText(/Expires:.*08:00/)).toBeInTheDocument(); expect(screen.getByText(/does not prove inbox/)).toBeInTheDocument();
    expect(sessionStorage.length).toBe(0); expect(localStorage.length).toBe(0);
  });
  it('requires explicit confirmation and cancellation sends no mutation', async () => {
    const mock = fetcher(reply(profile), reply(page())); mount(); await review();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus());
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  });
  it('confirms only a successful revocation acknowledgment then reloads canonical state', async () => {
    const mock = fetcher(reply(profile), reply(page()), reply(null, 204), reply(profile), reply(page([{ ...row, revokedAt: '2034-01-02T00:00:00Z' }])));
    mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    await screen.findByText('Revoked'); expect(screen.getByText('Invitation revocation confirmed.')).toBeInTheDocument();
    const calls = mock.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(calls).toHaveLength(1);
    expect(calls[0][0]).toContain(`/organizations/${org}/invitations/${row.id}`);
    expect(new Headers(calls[0][1]?.headers).get('X-StrataAI-Request')).toBe('1');
  });
  it('recovers a lost acknowledgment through read-only canonical review without another delete', async () => {
    const mock = fetcher(reply(profile), reply(page()), new Error('lost'), reply(profile), reply(page([{ ...row, revokedAt: '2034-01-02T00:00:00Z' }])));
    mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    await screen.findByText(/Revocation could not be confirmed/); expect(screen.queryByText('Invitation revocation confirmed.')).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'Check revocation' })); await screen.findByText('Invitation revocation confirmed.');
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  });
  it('does not treat an ambiguous not-found response as confirmed revocation', async () => {
    fetcher(reply(profile), reply(page()), reply({ title: 'private diagnostics' }, 404), reply(profile), reply(page()));
    mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Check revocation' }));
    await screen.findByText(/Revocation was not confirmed/); expect(screen.queryByText('Invitation revocation confirmed.')).not.toBeInTheDocument();
    expect(screen.queryByText('private diagnostics')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: `Revoke invitation for ${row.email}` })).toBeEnabled();
  });
  it('clears protected recipient information when the mutation loses authentication', async () => {
    fetcher(reply(profile), reply(page()), reply({}, 401)); mount(); await review();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' })); await screen.findByText(/Sign in again/);
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Email sent')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create invitation' })).not.toBeInTheDocument();
  });
  it('rejects malformed server role/surface data before displaying private rows', async () => {
    fetcher(reply(profile), reply(page([{ ...row, targetRole: 'SUPERUSER' }]))); mount();
    await screen.findByText(/Unable to confirm invitation history/); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('uses the authoritative cursor and allows navigation back to the first page', async () => {
    const rows = Array.from({ length: 50 }, (_, n) => ({ ...row, id: `20000000-0000-0000-0000-${(n + 1).toString().padStart(12, '0')}`, email: `issued-${n}@example.test` }));
    const last = rows.at(-1)!.id;
    const mock = fetcher(reply(profile), reply({ items: rows, nextCursor: last }), reply(profile), reply(page([{ ...row, id: '30000000-0000-0000-0000-000000000001' }])), reply(profile), reply({ items: rows, nextCursor: last }));
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Next invitations' })); await screen.findByText(row.email);
    expect(mock.mock.calls[4][0]).toContain(`?after=${last}`);
    fireEvent.click(screen.getByRole('button', { name: 'Previous invitations' })); await screen.findByText('issued-0@example.test');
    expect(mock.mock.calls[7][0]).not.toContain('?after=');
  });
});

const board = '30000000-0000-4000-8000-000000000003';
const boardScope = { board: { id: board, organizationId: org, name: 'Private maintenance', lifecycleState: 'active' }, access: { canAdminister: true } };
const boardRow = { ...row, boardTarget: { boardId: board, role: 'ADMIN' } };
function boardMount() {
  return render(<RouterProvider router={createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/invitations', element: <BoardInvitationHistoryPage /> }],
    { initialEntries: [`/app/${org}/boards/${board}/invitations`] })} />);
}
it.each(['Organization', 'Portal', 'Board'])('retires consent at %s invitation expiry and refreshes canonical history without a write', async surface => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2035-01-08T17:59:59Z'));
  const invitation = surface === 'Board' ? boardRow : surface === 'Portal'
    ? { ...row, surface: 'PORTAL', targetRole: 'OWNER' } : row;
  const mock = vi.fn(async (path: string) => reply(path === '/me' ? profile : path === `/boards/${board}`
    ? boardScope : { items: [invitation], nextCursor: null }));
  vi.stubGlobal('fetch', mock);
  await act(async () => { if (surface === 'Board') boardMount(); else mount(); });
  await act(async () => fireEvent.click(screen.getByRole('button', { name: `Revoke invitation for ${row.email}` })));
  expect(screen.getByRole('dialog')).toBeInTheDocument();
  await act(async () => vi.advanceTimersByTimeAsync(1000));
  expect(screen.getByText('Expired')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: `Revoke invitation for ${row.email}` })).not.toBeInTheDocument();
  await act(async () => vi.advanceTimersByTimeAsync(1000));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  const historyReads = () => mock.mock.calls.filter(call => call[0].endsWith('/invitations')).length;
  expect(historyReads()).toBe(2);
  await act(async () => vi.advanceTimersByTimeAsync(60_000));
  expect(historyReads()).toBe(2);
  expect(mock.mock.calls.some(call => (call as unknown as [string, RequestInit?])[1]?.method === 'DELETE')).toBe(false);
});
it('rechecks expiry after account admission even when the scheduled expiry callback has not run', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2035-01-08T17:59:59Z'));
  const mock = fetcher(reply(page()), reply(page()));
  await act(async () => { mount(); });
  await act(async () => fireEvent.click(screen.getByRole('button', { name: `Revoke invitation for ${row.email}` })));
  vi.setSystemTime(new Date(row.expiresAt));
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' })));
  expect(screen.getByText('Expired')).toBeInTheDocument();
  expect(screen.getByText('This invitation reached its expiry time. Review its current state.')).toBeInTheDocument();
  expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
});
it('withdraws Board invitation history and revocation consent when heartbeat admission loses administration', async () => {
  let withdrawn = false;
  const mock = vi.fn(async (path: string) => reply(path === '/me' ? profile : path === `/boards/${board}`
    ? { ...boardScope, access: { canAdminister: !withdrawn } } : { items: [boardRow], nextCursor: null }));
  vi.stubGlobal('fetch', mock); boardMount(); await review();
  withdrawn = true; await act(async () => boardLive.watch.mock.calls[0][0].status('live'));
  await screen.findByText('Board invitation administration is unavailable.');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  expect(screen.queryByText('Private maintenance')).not.toBeInTheDocument();
  expect(live.watch).not.toHaveBeenCalled();
});
it('shows the exact Board role and recovers a lost revocation from canonical history', async () => {
  const mock = fetcher(reply(profile), reply(boardScope), reply({ items: [boardRow], nextCursor: null }), new Error('Lost committed acknowledgment'),
    reply(profile), reply(boardScope), reply({ items: [{ ...boardRow, revokedAt: '2034-01-02T00:00:00Z' }], nextCursor: null }));
  boardMount(); await screen.findByRole('heading', { name: 'Private maintenance' });
  expect(screen.getByText('Board access: admin')).toBeVisible();
  await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
  await screen.findByText('Revocation could not be confirmed. Check the current invitation state before another action.');
  expect(screen.queryByRole('heading', { name: 'Private maintenance' })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Check revocation' }));
  await screen.findByText('Invitation revocation confirmed.');
  const writes = mock.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(writes).toHaveLength(1);
  expect(writes[0][0]).toBe(`/boards/${board}/invitations/${row.id}?expectedActorId=${profile.id}`);
  expect(screen.getByText('Revoked')).toBeVisible();
});
it.each([
  { ...boardRow, boardTarget: null },
  { ...boardRow, boardTarget: { boardId: org, role: 'ADMIN' } },
  { ...boardRow, boardTarget: { boardId: board, role: 'OWNER' } },
  { ...boardRow, surface: 'PORTAL', targetRole: 'OWNER' },
])('rejects history metadata outside the exact Board contract: %j', async value => {
  fetcher(reply(profile), reply(boardScope), reply({ items: [value], nextCursor: null })); boardMount();
  await screen.findByText('Unable to confirm invitation history. Please retry.');
  expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Private maintenance' })).not.toBeInTheDocument();
});
it('purges private Board metadata when current authority denies revocation', async () => {
  fetcher(reply(profile), reply(boardScope), reply({ items: [boardRow], nextCursor: null }), reply({ code: 'board_not_found' }, 404));
  boardMount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
  await screen.findByText('Board invitation administration is unavailable.');
  expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Private maintenance' })).not.toBeInTheDocument();
});
it('refuses a Board belonging to a different Organization before reading its history', async () => {
  const mock = fetcher(reply(profile), reply({ ...boardScope, board: { ...boardScope.board, organizationId: board } }));
  boardMount(); await screen.findByText('Board invitation administration is unavailable.'); expect(mock).toHaveBeenCalledTimes(2);
});
