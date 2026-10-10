import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

import { AuthPage } from './AuthPage';

describe('PRD-02 authentication UI', () => {
  it('binds invitation signup retries to the proof without storing or displaying it', async () => {
    const invitationToken = 'private_invitation_proof_12345678901234567890';
    const fetch = vi.fn().mockRejectedValueOnce(new Error('Lost registration acknowledgment')).mockResolvedValueOnce(new Response(JSON.stringify({
      user: { id: '11111111-1111-4111-8111-111111111111', email: 'person@example.test', emailVerified: false },
    }), { status: 201 })); vi.stubGlobal('fetch', fetch);
    render(<MemoryRouter><AuthPage invitationToken={invitationToken} /></MemoryRouter>);
    fireEvent.click(screen.getByRole('tab', { name: 'Register' }));
    fireEvent.change(screen.getByLabelText(/^Display name/), { target: { value: 'Person' } });
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText(/Retry with the same details/);
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText('Account created. Verify your email, then reopen your invitation to sign in and accept it.');
    const first = fetch.mock.calls[0][1] as RequestInit; const second = fetch.mock.calls[1][1] as RequestInit;
    expect(JSON.parse(first.body as string).invitationToken).toBe(invitationToken);
    expect(second.body).toBe(first.body); expect(new Headers(second.headers).get('Idempotency-Key')).toBe(new Headers(first.headers).get('Idempotency-Key'));
    expect(document.body.textContent).not.toContain(invitationToken);
    expect(JSON.stringify({ ...localStorage, ...sessionStorage })).not.toContain(invitationToken);
    expect(screen.getByLabelText(/^Password/)).toHaveValue('');
  });

  afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

  it.each(['login', 'register'])('PRD-02/24: preserves %s intent after an abuse limit without falsely acknowledging success', async mode => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 429 })));
    vi.stubGlobal('fetch', fetch);
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    if (mode === 'register') {
      fireEvent.click(screen.getByRole('tab', { name: 'Register' }));
      fireEvent.change(screen.getByLabelText(/^Display name/), { target: { value: 'Person' } });
    }
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    const button = screen.getByRole('button', { name: mode === 'register' ? 'Create account' : 'Sign in' });
    fireEvent.click(button);
    await screen.findByText('Too many attempts. Please wait before retrying with the same details.');
    expect(screen.getByLabelText(/^Password/)).toHaveValue('correct-private-password');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    fireEvent.click(button);
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    const first = fetch.mock.calls[0][1] as RequestInit; const second = fetch.mock.calls[1][1] as RequestInit;
    expect(new Headers(second.headers).get('Idempotency-Key')).toBe(new Headers(first.headers).get('Idempotency-Key'));
    expect(second.body).toBe(first.body);
  });

  it('retries an uncertain registration with the same key and clears details only after a valid acknowledgment', async () => {
    const fetch = vi.fn().mockRejectedValueOnce(new Error('lost creation response')).mockResolvedValueOnce(new Response(JSON.stringify({
      user: { id: '11111111-1111-4111-8111-111111111111', email: 'person@example.test', emailVerified: true },
    }), { status: 201 }));
    vi.stubGlobal('fetch', fetch);
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole('tab', { name: 'Register' }));
    fireEvent.change(screen.getByLabelText(/^Display name/), { target: { value: 'Person' } });
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText(/Registration could not be confirmed/);
    expect(screen.getByLabelText(/^Password/)).toHaveValue('correct-private-password');
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText('Account created. Sign in to continue.');
    expect(screen.getByLabelText(/^Password/)).toHaveValue('');
    const first = fetch.mock.calls[0][1] as RequestInit; const second = fetch.mock.calls[1][1] as RequestInit;
    expect(new Headers(first.headers).get('Idempotency-Key')).toBe(new Headers(second.headers).get('Idempotency-Key'));
    expect(second.body).toBe(first.body);
  });

  it('preserves registration input when a creation response cannot prove the account', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 201 })));
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.click(screen.getByRole('tab', { name: 'Register' }));
    fireEvent.change(screen.getByLabelText(/^Display name/), { target: { value: 'Person' } });
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText(/Registration could not be confirmed/);
    expect(screen.getByLabelText(/^Password/)).toHaveValue('correct-private-password');
    expect(screen.queryByText('Account created. Sign in to continue.')).not.toBeInTheDocument();
  });

  it('requires an explicit fresh attempt after an expired acknowledgment', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ code: 'idempotency_key_expired' }), { status: 409 })));
    vi.stubGlobal('fetch', fetch);
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByRole('button', { name: 'Start a new sign-in attempt' });
    const first = new Headers((fetch.mock.calls[0][1] as RequestInit).headers).get('Idempotency-Key');
    fireEvent.click(screen.getByRole('button', { name: 'Start a new sign-in attempt' }));
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(new Headers((fetch.mock.calls[1][1] as RequestInit).headers).get('Idempotency-Key')).not.toBe(first);
  });

  it('rejects a success acknowledgment for another email', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      user: { id: '11111111-1111-4111-8111-111111111111', email: 'other@example.test' },
      sessionExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    }), { status: 200 })));
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText(/Retry with the same details/);
    expect(screen.getByRole('heading', { name: 'StrataAI2' })).toBeInTheDocument();
  });

  it('bounds a stalled response body and preserves the same retry key', async () => {
    vi.useFakeTimers();
    const fetch = vi.fn().mockResolvedValue({ ok: true, json: () => new Promise(() => {}) });
    vi.stubGlobal('fetch', fetch);
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
    expect(screen.getByText(/Retry with the same details/)).toBeInTheDocument();
    const first = fetch.mock.calls[0][1] as RequestInit;
    expect(first.signal?.aborted).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(new Headers((fetch.mock.calls[1][1] as RequestInit).headers).get('Idempotency-Key')).toBe(new Headers(first.headers).get('Idempotency-Key'));
    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  });

  it('retains the login intent after a lost response and replaces it when credentials change', async () => {
    const requests: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn((_path: string, options: RequestInit) => {
      requests.push(options);
      return Promise.reject(new Error('lost response'));
    }));
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText(/Retry with the same details/);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled());
    expect(new Headers(requests[0].headers).get('Idempotency-Key')).toBe(new Headers(requests[1].headers).get('Idempotency-Key'));
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'changed-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(requests).toHaveLength(3));
    expect(new Headers(requests[2].headers).get('Idempotency-Key')).not.toBe(new Headers(requests[0].headers).get('Idempotency-Key'));
  });

  it('masks untrusted server titles and aborts an owned request on unmount', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ title: 'private database detail', code: 'unknown' }), { status: 503 }));
    vi.stubGlobal('fetch', fetch);
    const view = render(<MemoryRouter><AuthPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText('Authentication could not be confirmed. Please try again.');
    expect(screen.queryByText('private database detail')).not.toBeInTheDocument();
    fetch.mockImplementationOnce(() => new Promise(() => {}));
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    const options = fetch.mock.calls[1][1] as RequestInit;
    view.unmount();
    expect(options.signal?.aborted).toBe(true);
  });

  it('supports switching between sign-in and registration forms', () => {
    render(
      <MemoryRouter>
        <AuthPage />
      </MemoryRouter>,
    );

    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Create account' }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Register' }));

    expect(
      screen.getByRole('button', { name: 'Create account' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Use at least 12 characters/i),
    ).toBeInTheDocument();
  });
});


describe('PRD-02 authentication support references', () => {
  afterEach(() => vi.unstubAllGlobals());
  it.each(['login', 'register'])('pairs %s errors with the actual safe response reference', async mode => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'private-provider-detail' }), {
      status: 503, headers: { 'X-Correlation-ID': 'auth.response-1' },
    })));
    render(<MemoryRouter><AuthPage /></MemoryRouter>);
    if (mode === 'register') {
      fireEvent.click(screen.getByRole('tab', { name: 'Register' }));
      fireEvent.change(screen.getByLabelText(/^Display name/), { target: { value: 'Person' } });
    }
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: mode === 'login' ? 'Sign in' : 'Create account' }));
    await screen.findByText('Reference: auth.response-1');
    expect(screen.getByText('Authentication could not be confirmed. Please try again.')).toBeVisible();
    expect(screen.queryByText(/private-provider-detail/)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^Password/)).toHaveValue('correct-private-password');
  });
});


it('PRD-02: retains a received response reference for an unreadable login acknowledgment', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, headers: new Headers({ 'X-Correlation-ID': 'login.unreadable-1' }),
    json: () => Promise.reject(new Error('private-body')) }));
  render(<MemoryRouter><AuthPage /></MemoryRouter>);
  fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
  fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  await screen.findByText('Reference: login.unreadable-1');
  expect(screen.getByText(/Retry with the same details/)).toBeVisible();
  expect(screen.queryByText(/private-body/)).not.toBeInTheDocument();
  vi.unstubAllGlobals();
});
it('PRD-02: clears a prior refusal reference on retry and rejects malformed new metadata', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 429, headers: { 'X-Correlation-ID': 'login.refusal-1' } }))
    .mockResolvedValueOnce(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'private:diagnostic' } }));
  vi.stubGlobal('fetch', fetch); render(<MemoryRouter><AuthPage /></MemoryRouter>);
  fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
  fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  await screen.findByText('Reference: login.refusal-1');
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  expect(screen.queryByText(/login.refusal-1/)).not.toBeInTheDocument();
  await screen.findByText('Authentication could not be confirmed. Please try again.');
  expect(screen.queryByText(/Reference:|private:diagnostic/)).not.toBeInTheDocument();
  expect(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key'));
  vi.unstubAllGlobals();
});
