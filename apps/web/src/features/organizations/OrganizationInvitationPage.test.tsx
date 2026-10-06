import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationInvitationPage, BoardInvitationPage } from './OrganizationInvitationPage';
import { forgetInvitationIntents, invitationIntentKey } from './invitationIntent';
const org = '10000000-0000-0000-0000-000000000000'; const actor = '20000000-0000-0000-0000-000000000000';
const profile = { id: actor, locale: 'en-CA', timezone: 'America/Vancouver' };
const admission = { organizationId: org, actorRole: 0, member: { userId: actor, role: 0 } };
const input = { email: 'invite@example.test', surface: 'INTERNAL', targetRole: 'MEMBER' };
const ack = { id: '30000000-0000-0000-0000-000000000000', organizationId: org, ...input, expiresAt: '2026-10-08T18:00:00Z', invitationToken: null };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
// Stable-account fixtures keep command response queues separate from the
// additional profile checks. Race cases below exercise those reads explicitly.
function stubFetch(mock: (path: string, options?: RequestInit) => Promise<Response>) {
  let initialProfile = true;
  vi.stubGlobal('fetch', (path: string, options?: RequestInit) => {
    if (path === '/me') {
      if (!initialProfile) return Promise.resolve(reply(profile));
      initialProfile = false;
    }
    return mock(path, options);
  });
}
const key = '40000000-0000-0000-0000-000000000000'; const storedKey = invitationIntentKey(actor, org);
function mount() {
  const router = createMemoryRouter([
    { path: '/app/:organizationId/invite', element: <OrganizationInvitationPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> }, { path: '/elsewhere', element: <h1>Other destination</h1> },
  ], { initialEntries: [`/app/${org}/invite`] }); render(<RouterProvider router={router} />); return router;
}
function fetcher(...responses: (Response | Error)[]) {
  const mock = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(admission));
  for (const response of responses) { if (response instanceof Error) mock.mockRejectedValueOnce(response); else mock.mockResolvedValueOnce(response); }
  stubFetch(mock); return mock;
}
async function submit() { fireEvent.change(await screen.findByLabelText(/^Invitation email/), { target: { value: input.email } }); fireEvent.click(screen.getByRole('button', { name: 'Create invitation' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); vi.useRealTimers(); sessionStorage.clear(); });
describe('Administrator invitation intent and creation acknowledgment', () => {
  it.each([
    [false, false, 'unavailable'], [true, false, 'unavailable'],
    [false, true, 'unavailable'], [true, true, 'unavailable'],
    [false, true, 'malformed'], [true, true, 'network'],
  ] as const)('withdraws private display on an unconfirmed account and recovers only the original request (Board=%s, committed=%s, %s)', async (boardSurface, committed, failure) => {
    let profiles = 0;
    const mock = vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') {
        if (++profiles === (committed ? 4 : 3)) {
          if (failure === 'network') throw new Error('temporary profile transport failure');
          return reply(failure === 'malformed' ? { id: 'invalid' } : {}, failure === 'unavailable' ? 503 : 200);
        }
        return reply(profile);
      }
      if (options?.method === 'POST') return reply(boardSurface ? boardAck : ack, 201);
      return reply(boardSurface ? boardAdmission : admission);
    });
    vi.stubGlobal('fetch', mock); if (boardSurface) boardMount(); else mount();
    await submit();
    await screen.findByRole('button', { name: 'Retry permission check' });
    expect(screen.queryByDisplayValue(input.email)).not.toBeInTheDocument();
    expect(screen.queryByText('Private maintenance')).not.toBeInTheDocument();
    expect(screen.queryByText('Invitation creation acknowledged.')).not.toBeInTheDocument();
    const scopeKey = storedKey + (boardSurface ? `:board:${board}` : '');
    const saved = sessionStorage.getItem(scopeKey)!;
    expect(JSON.parse(saved).input).toEqual(input);
    expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(committed ? 1 : 0);
    fireEvent.click(screen.getByRole('button', { name: 'Retry permission check' }));
    await screen.findByText(/prior invitation request is awaiting acknowledgment/);
    expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(committed ? 1 : 0);
    fireEvent.click(screen.getByRole('button', { name: 'Retry same invitation' }));
    await screen.findByText('Invitation creation acknowledged.');
    const posts = mock.mock.calls.filter(call => call[1]?.method === 'POST');
    expect(posts).toHaveLength(committed ? 2 : 1);
    for (const post of posts) expect(new Headers(post[1]?.headers).get('Idempotency-Key')).toBe(JSON.parse(saved).key);
  });
  it.each([false, true])('withholds saved input and Board names after account replacement during admission (Board=%s)', async boardSurface => {
    let current = profile;
    const scopeKey = storedKey + (boardSurface ? `:board:${board}` : '');
    sessionStorage.setItem(scopeKey, JSON.stringify({ key, input }));
    const mock = vi.fn(async (path: string) => {
      if (path === '/me') return reply(current);
      current = { ...profile, id: ack.id }; return reply(boardSurface ? boardAdmission : admission);
    });
    vi.stubGlobal('fetch', mock); if (boardSurface) boardMount(); else mount();
    await screen.findByText('Sign in destination');
    expect(screen.queryByDisplayValue(input.email)).not.toBeInTheDocument();
    expect(screen.queryByText('Private maintenance')).not.toBeInTheDocument();
    expect(sessionStorage.getItem(scopeKey)).not.toBeNull();
  });
  it.each([false, true])('sends no invitation when the reviewing account changes before submission (Board=%s)', async boardSurface => {
    let current = profile;
    const mock = vi.fn(async (path: string) => reply(path === '/me' ? current : boardSurface ? boardAdmission : admission));
    vi.stubGlobal('fetch', mock); if (boardSurface) boardMount(); else mount();
    await screen.findByLabelText(/^Invitation email/); current = { ...profile, id: ack.id };
    await submit(); await screen.findByText('Sign in destination');
    expect(mock.mock.calls.filter(call => call[0].includes('/invitations'))).toHaveLength(0);
    expect(JSON.parse(sessionStorage.getItem(storedKey + (boardSurface ? `:board:${board}` : ''))!).input).toEqual(input);
  });
  it.each([false, true])('withholds committed acknowledgment after account replacement and preserves original recovery (Board=%s)', async boardSurface => {
    let current = profile;
    const mock = vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') return reply(current);
      if (options?.method === 'POST') { current = { ...profile, id: ack.id }; return reply(boardSurface ? boardAck : ack, 201); }
      return reply(boardSurface ? boardAdmission : admission);
    });
    vi.stubGlobal('fetch', mock); if (boardSurface) boardMount(); else mount();
    await submit(); await screen.findByText('Sign in destination');
    expect(screen.queryByText('Invitation creation acknowledged.')).not.toBeInTheDocument();
    expect(screen.queryByText(input.email)).not.toBeInTheDocument();
    const posts = mock.mock.calls.filter(call => call[1]?.method === 'POST'); expect(posts).toHaveLength(1);
    expect(posts[0][0]).toContain(`expectedActorId=${actor}`);
    expect(JSON.parse(sessionStorage.getItem(storedKey + (boardSurface ? `:board:${board}` : ''))!).input).toEqual(input);
  });
  it('retains a token-free command and validates creation without claiming delivery or recipient access', async () => {
    const mock = fetcher(reply(ack, 201)); mount(); await submit(); await screen.findByText('Invitation creation acknowledged.');
    const saved = JSON.parse(sessionStorage.getItem(storedKey)!); expect(saved.input).toEqual(input);
    const sent = mock.mock.calls[2]; expect(sent[1].headers.get('Idempotency-Key')).toBe(saved.key); expect(sent[1].headers.get('X-StrataAI-Request')).toBe('1');
    expect(sent[0]).toBe(`/organizations/${org}/invitations?expectedActorId=${actor}`);
    expect(JSON.parse(sent[1].body)).toEqual(input); expect(screen.getByText(/Email delivery is not confirmed here/)).toBeInTheDocument();
    expect(screen.getByText(/Expires:.*11:00/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Create another invitation' })); expect(sessionStorage.getItem(storedKey)).toBeNull();
    expect(screen.getByLabelText(/^Invitation email/)).toHaveValue('');
  });
  it('retries lost acknowledgment with the identical key/body and locks grant edits until resolved', async () => {
    const mock = fetcher(new Error('lost'), reply(ack, 201)); mount(); await submit(); await screen.findByText(/invitation could not be confirmed/);
    expect(screen.getByLabelText(/^Invitation email/)).toBeDisabled(); fireEvent.click(screen.getByRole('button', { name: 'Retry same invitation' }));
    await screen.findByText('Invitation creation acknowledged.'); const calls = mock.mock.calls.filter(call => call[1]?.method === 'POST'); expect(calls).toHaveLength(2);
    expect(calls[0][1].headers.get('Idempotency-Key')).toBe(calls[1][1].headers.get('Idempotency-Key')); expect(calls[0][1].body).toBe(calls[1][1].body);
  });
  it('preserves the same request after throttling without exposing provider error details', async () => {
    const mock = fetcher(reply({ title: 'private provider data' }, 429), reply(ack, 201)); mount(); await submit();
    await screen.findByText(/Too many requests.*wait before retrying this same invitation/);
    expect(screen.queryByText('private provider data')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry same invitation' })); await screen.findByText('Invitation creation acknowledged.');
    expect(mock.mock.calls[2][1].headers.get('Idempotency-Key')).toBe(mock.mock.calls[3][1].headers.get('Idempotency-Key'));
    expect(mock.mock.calls[2][1].body).toBe(mock.mock.calls[3][1].body);
  });
  it('fences completion after navigation while retaining the pending intent for later authorized recovery', async () => {
    let resolve!: (response: Response) => void; const mock = fetcher(); mock.mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }));
    const router = mount(); await submit(); const saved = sessionStorage.getItem(storedKey);
    await waitFor(() => expect(resolve).toBeDefined());
    await act(async () => { await router.navigate('/elsewhere'); }); await act(async () => resolve(reply(ack, 201)));
    expect(screen.getByText('Other destination')).toBeInTheDocument(); expect(screen.queryByText('Invitation creation acknowledged.')).not.toBeInTheDocument();
    expect(sessionStorage.getItem(storedKey)).toBe(saved);
  });
  it('rehydrates only a freshly authorized actor/Organization intent after navigation', async () => {
    sessionStorage.setItem(storedKey, JSON.stringify({ key, input })); const mock = fetcher(reply(ack, 201)); mount();
    await screen.findByText(/prior invitation request is awaiting acknowledgment/); expect(screen.getByLabelText(/^Invitation email/)).toHaveValue(input.email);
    expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Retry same invitation' })); await screen.findByText('Invitation creation acknowledged.');
    expect(mock.mock.calls[2][1].headers.get('Idempotency-Key')).toBe(key);
  });
  it.each([401, 403, 404])('clears private input and acknowledgment after creation loses current access (%s)', async status => {
    fetcher(reply({}, status)); mount(); await submit(); await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization invitations are unavailable to your account.');
    expect(screen.queryByLabelText(/^Invitation email/)).not.toBeInTheDocument(); expect(screen.queryByText(input.email)).not.toBeInTheDocument();
  });
  it('does not disclose a retained intent to an unauthorized direct route', async () => {
    sessionStorage.setItem(storedKey, JSON.stringify({ key, input }));
    stubFetch(vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({}, 404))); mount();
    await screen.findByText('Organization invitations are unavailable to your account.'); expect(screen.queryByDisplayValue(input.email)).not.toBeInTheDocument();
  });
  it('does not load another actor or Organization pending input', async () => {
    sessionStorage.setItem(invitationIntentKey(ack.id, org), JSON.stringify({ key, input })); sessionStorage.setItem(invitationIntentKey(actor, ack.id), JSON.stringify({ key, input }));
    fetcher(); mount(); expect(await screen.findByLabelText(/^Invitation email/)).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Create invitation' })).toBeEnabled();
  });
  it.each(['idempotency_key_reused', 'idempotency_key_expired'])('keeps an unresolved %s key reserved rather than silently creating another request', async code => {
    const mock = fetcher(reply({ code }, 409)); mount(); await submit(); await screen.findByText(/This request cannot be retried/);
    expect(screen.getByRole('button', { name: 'Retry same invitation' })).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Create another invitation' })).not.toBeInTheDocument();
    expect(JSON.parse(sessionStorage.getItem(storedKey)!).input).toEqual(input); expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
  });
  it('permits corrections only after an acknowledged validation failure with no created invitation', async () => {
    const mock = fetcher(reply({ code: 'invalid_email' }, 400), reply({ ...ack, email: 'corrected@example.test' }, 201)); mount(); await submit();
    await screen.findByText(/Check the invitation email/); expect(screen.getByLabelText(/^Invitation email/)).toBeEnabled(); expect(sessionStorage.getItem(storedKey)).toBeNull();
    fireEvent.change(screen.getByLabelText(/^Invitation email/), { target: { value: 'corrected@example.test' } }); fireEvent.click(screen.getByRole('button', { name: 'Create invitation' }));
    await screen.findByText('Invitation creation acknowledged.'); expect(mock.mock.calls[2][1].headers.get('Idempotency-Key')).not.toBe(mock.mock.calls[3][1].headers.get('Idempotency-Key'));
  });
  it('rejects mismatched or bearer-bearing creation acknowledgments without false success', async () => {
    fetcher(reply({ ...ack, invitationToken: 'untrusted-bearer' }, 201)); mount(); await submit(); await screen.findByText(/invitation could not be confirmed/);
    expect(screen.queryByText('Invitation creation acknowledged.')).not.toBeInTheDocument(); expect(screen.queryByText('untrusted-bearer')).not.toBeInTheDocument();
  });
  it('bounds an abort-ignoring request, suppresses duplicate submits and fences its late acknowledgment', async () => {
    let resolve!: (response: Response) => void; const mock = fetcher(); mock.mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; }));
    mount(); await screen.findByLabelText(/^Invitation email/); vi.useFakeTimers();
    fireEvent.change(screen.getByLabelText(/^Invitation email/), { target: { value: input.email } });
    fireEvent.click(screen.getByRole('button', { name: 'Create invitation' })); fireEvent.submit(screen.getByLabelText(/^Invitation email/).closest('form')!);
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000); }); expect(screen.getByText(/invitation could not be confirmed/)).toBeInTheDocument();
    await act(async () => resolve(reply(ack, 201))); expect(screen.queryByText('Invitation creation acknowledged.')).not.toBeInTheDocument();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
  });
  it('fails closed when a pending command cannot be stored or read', async () => {
    const mock = fetcher(); const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage unavailable'); });
    const router = mount(); await submit(); await screen.findByText(/browser could not retain the invitation request/); expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(0);
    set.mockRestore(); sessionStorage.setItem(storedKey, 'unreadable');
    mock.mockResolvedValueOnce(reply(admission));
    await act(async () => { await router.navigate('/elsewhere'); }); await act(async () => { await router.navigate(`/app/${org}/invite`); });
    await screen.findByText(/saved invitation request cannot be read/); expect(screen.getByRole('button', { name: 'Create invitation' })).toBeDisabled();
    expect(mock.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(0);
    // Explicit utility cleanup does not touch unrelated temporary site data.
    sessionStorage.setItem('unrelated', 'keep'); forgetInvitationIntents(); expect(sessionStorage.getItem(storedKey)).toBeNull(); expect(sessionStorage.getItem('unrelated')).toBe('keep');
  });
});

const board = '50000000-0000-4000-8000-000000000000';
const boardAdmission = { board: { id: board, organizationId: org, name: 'Private maintenance', lifecycleState: 'active' }, access: { canAdminister: true } };
const boardAck = { ...ack, boardTarget: { boardId: board, role: 'MEMBER' } };
function boardMount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/invite', element: <BoardInvitationPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> }], { initialEntries: [`/app/${org}/boards/${board}/invite`] });
  render(<RouterProvider router={router} />); return router;
}
it('binds a Board creation draft and lost acknowledgment retry to the exact Board payload', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(boardAdmission))
    .mockRejectedValueOnce(new Error('Lost acknowledgment')).mockResolvedValueOnce(reply(boardAck, 201));
  stubFetch(mock); boardMount(); await submit();
  await screen.findByText(/invitation could not be confirmed/);
  expect(screen.queryByLabelText('Access surface')).not.toBeInTheDocument();
  expect(screen.getByLabelText(/^Invitation email/)).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Retry same invitation' }));
  await screen.findByText('Invitation creation acknowledged.');
  const calls = mock.mock.calls.filter(call => call[1]?.method === 'POST');
  expect(calls).toHaveLength(2); expect(calls[0][0]).toBe(`/boards/${board}/invitations?expectedActorId=${actor}`);
  expect(calls[0][1].body).toBe(calls[1][1].body);
  expect(JSON.parse(calls[0][1].body)).toEqual({ email: input.email, role: 'MEMBER' });
  expect(calls[0][1].headers.get('Idempotency-Key')).toBe(calls[1][1].headers.get('Idempotency-Key'));
  expect(sessionStorage.getItem(`${storedKey}:board:${board}`)).not.toBeNull();
  expect(sessionStorage.getItem(storedKey)).toBeNull();
  expect(screen.getByText('Board: Member')).toBeVisible();
});
it.each([
  { ...boardAck, boardTarget: null },
  { ...boardAck, boardTarget: { boardId: org, role: 'MEMBER' } },
  { ...boardAck, boardTarget: { boardId: board, role: 'ADMIN' } },
])('refuses creation confirmation for a mismatched Board target: %j', async value => {
  stubFetch(vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(boardAdmission)).mockResolvedValueOnce(reply(value, 201)));
  boardMount(); await submit(); await screen.findByText(/invitation could not be confirmed/);
  expect(screen.queryByText('Invitation creation acknowledged.')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Retry same invitation' })).toBeEnabled();
});
it('hides Board name and controls after current access denies creation', async () => {
  stubFetch(vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(boardAdmission)).mockResolvedValueOnce(reply({}, 404)));
  boardMount(); await submit(); await screen.findByText('Board invitations are unavailable to your account.');
  expect(screen.queryByRole('heading', { name: 'Private maintenance' })).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/^Invitation email/)).not.toBeInTheDocument();
});
it('requires current Board administration even if the Board itself can be viewed', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({ ...boardAdmission, access: { canAdminister: false } }));
  stubFetch(mock); boardMount(); await screen.findByText('Board invitations are unavailable to your account.');
  expect(screen.queryByLabelText(/^Invitation email/)).not.toBeInTheDocument(); expect(mock).toHaveBeenCalledTimes(2);
});
