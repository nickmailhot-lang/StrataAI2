import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProfilePage } from './ProfilePage';
import { AuthPage } from './AuthPage';
vi.mock('./identityLive', () => ({ watchIdentity: () => () => {} }));

const profile = { id: '00000000-0000-4000-8000-000000000001', email: 'private-owner@example.test', displayName: 'Private owner', avatarUrl: null, locale: 'en-CA', timezone: 'America/Vancouver', status: 'ACTIVE', emailVerified: true, version: 1, createdAt: '2026-03-08T09:30:00Z', updatedAt: '2026-03-08T10:30:00Z' };
const snapshot = () => new Response(JSON.stringify({ profile, cursor: 1, latestSequence: 1, hasMore: false, events: [] }));
function renderProfile() {
  return render(<MemoryRouter initialEntries={['/profile']}><Routes><Route path="/profile" element={<ProfilePage />} /><Route path="/login" element={<AuthPage />} /></Routes></MemoryRouter>);
}
async function confirm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Deactivate account' }));
  await screen.findByRole('dialog', { name: 'Deactivate your account?' });
  fireEvent.click(screen.getByRole('button', { name: 'Confirm deactivation' }));
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

describe('PRD-02/03/18 account deactivation', () => {
  it('requires explicit confirmation and focuses the safe cancellation action', async () => {
    const fetchMock = vi.fn().mockResolvedValue(snapshot()); vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    fireEvent.click(await screen.findByRole('button', { name: 'Deactivate account' }));
    await screen.findByRole('dialog', { name: 'Deactivate your account?' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Keep account active' })).toHaveFocus());
    expect(screen.getByText(/future sign-in will be blocked/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Keep account active' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it.each(['organization_owner_required', 'ownership_changed'])('preserves edits on %s and reuses the refused intent after fresh confirmation', async code => {
    const pendingInvitation = 'strataai:invitation-create:v1:actor:organization';
    const pendingConfiguration = 'strataai:configuration-change:v1:actor:organization';
    sessionStorage.setItem(pendingConfiguration, 'pending original configuration');
    sessionStorage.setItem(pendingInvitation, 'pending private invitation input');
    const fetchMock = vi.fn().mockResolvedValueOnce(snapshot())
      .mockResolvedValueOnce(new Response(JSON.stringify({ code, title: 'Private organization detail must not appear' }), { status: 409 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock); renderProfile();
    await screen.findByRole('button', { name: 'Deactivate account' });
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Preserved draft' } });
    await confirm();
    await screen.findByText(code === 'organization_owner_required' ? /Another active owner must/ : /Your organization ownership changed/);
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Preserved draft');
    expect(sessionStorage.getItem(pendingInvitation)).toBe('pending private invitation input');
    expect(sessionStorage.getItem(pendingConfiguration)).toBe('pending original configuration');
    expect(screen.queryByText('Private organization detail must not appear')).not.toBeInTheDocument();
    await confirm();
    await screen.findByText('Your account is deactivated. Historical activity is preserved.');
    expect(sessionStorage.getItem(pendingInvitation)).toBeNull();
    expect(sessionStorage.getItem(pendingConfiguration)).toBeNull();
    expect(new Headers(fetchMock.mock.calls[2][1].headers).get('Idempotency-Key')).toBe(new Headers(fetchMock.mock.calls[1][1].headers).get('Idempotency-Key'));
  });

  it('preserves a lost acknowledgment retry while suppressing reads and private profile details', async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn().mockResolvedValueOnce(snapshot()).mockRejectedValueOnce(new Error('Lost committed response'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock); renderProfile();
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Deactivate account' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm deactivation' }));
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByRole('button', { name: 'Retry deactivation' })).toBeEnabled();
    expect(screen.queryByText(profile.email)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Display name/)).not.toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(20_000));
    fireEvent(window, new Event('focus')); fireEvent(window, new Event('online'));
    await act(async () => { await Promise.resolve(); });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    fireEvent.click(screen.getByRole('button', { name: 'Retry deactivation' }));
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByText('Your account is deactivated. Historical activity is preserved.')).toBeInTheDocument();
    const key = new Headers(fetchMock.mock.calls[1][1].headers).get('Idempotency-Key');
    expect(key).toMatch(/^[0-9a-f-]{36}$/i);
    expect(new Headers(fetchMock.mock.calls[2][1].headers).get('Idempotency-Key')).toBe(key);
    expect(fetchMock.mock.calls[2][0]).toBe('/me/deactivate');
    for (const index of [1, 2]) expect(new Headers(fetchMock.mock.calls[index][1].headers).get('X-StrataAI-Expected-User')).toBe(profile.id);
  });

  it('does not treat an unexpected successful response as a deactivation acknowledgment', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(snapshot()).mockResolvedValueOnce(new Response(JSON.stringify(profile)));
    vi.stubGlobal('fetch', fetchMock); renderProfile(); await confirm();
    await screen.findByRole('button', { name: 'Retry deactivation' });
    expect(screen.queryByText('Your account is deactivated. Historical activity is preserved.')).not.toBeInTheDocument();
  });

  it('clears protected details on current authorization denial without claiming deactivation', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(snapshot()).mockRejectedValueOnce(new Error('Lost response'))
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock); renderProfile(); await confirm();
    fireEvent.click(await screen.findByRole('button', { name: 'Retry deactivation' }));
    await screen.findByRole('button', { name: 'Sign in' });
    expect(screen.queryByText(profile.email)).not.toBeInTheDocument();
    expect(screen.queryByText('Your account is deactivated. Historical activity is preserved.')).not.toBeInTheDocument();
  });

  it('bounds an abort-ignoring request and ignores a late deactivation acknowledgment after unmount', async () => {
    vi.useFakeTimers();
    let finish: ((response: Response) => void) | undefined;
    const stalled = new Promise<Response>(resolve => { finish = resolve; });
    const fetchMock = vi.fn().mockResolvedValueOnce(snapshot()).mockImplementationOnce(() => stalled);
    vi.stubGlobal('fetch', fetchMock); const view = renderProfile();
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Deactivate account' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm deactivation' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm deactivation' }));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(fetchMock.mock.calls[1][1].signal.aborted).toBe(true);
    expect(screen.getByRole('button', { name: 'Retry deactivation' })).toBeEnabled();
    view.unmount();
    await act(async () => { finish!(new Response(null, { status: 204 })); await stalled; });
    expect(screen.queryByText('Your account is deactivated. Historical activity is preserved.')).not.toBeInTheDocument();
  });
});


it.each([409, 503])('PRD-02/03: pairs a deactivation refusal or uncertain response with its own reference (%s)', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(snapshot()).mockResolvedValueOnce(new Response(JSON.stringify({ code: 'organization_owner_required', title: 'private-diagnostic' }), { status, headers: { 'X-Correlation-ID': 'deactivation.response-1' } })));
  renderProfile(); await confirm(); await screen.findByText('Reference: deactivation.response-1');
  expect(screen.queryByText(/private-diagnostic/)).not.toBeInTheDocument();
  if (status === 409) expect(await screen.findByRole('button', { name: 'Deactivate account' })).toBeEnabled();
  else expect(screen.getByRole('button', { name: 'Retry deactivation' })).toBeEnabled();
});

it('PRD-02/03: reveals an accessible focused recovery action when the uncertainty reference is announced', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(snapshot()).mockResolvedValueOnce(new Response('{}', {
    status: 503, headers: { 'X-Correlation-ID': 'deactivation.recovery-focus' },
  })));
  renderProfile(); await confirm(); await screen.findByText('Reference: deactivation.recovery-focus');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  const retry = screen.getByRole('button', { name: 'Retry deactivation' }); expect(retry).toBeEnabled(); expect(retry).toHaveFocus();
  expect(screen.queryByText(profile.email)).not.toBeInTheDocument(); expect(screen.queryByLabelText('Display name')).not.toBeInTheDocument();
});
