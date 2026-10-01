import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { PasswordRecoveryPage } from './PasswordRecoveryPage';
import { ResetPasswordPage } from './ResetPasswordPage';

afterEach(() => vi.unstubAllGlobals());
function LocationProbe() { const location = useLocation(); return <output aria-label="Location">{location.pathname + location.search + location.hash}</output>; }
function resetPage(path = '/reset-password#token=single-use-token') {
  render(<MemoryRouter initialEntries={[path]}><ResetPasswordPage /><LocationProbe /></MemoryRouter>);
}
function fillPasswords(confirm = 'new-correct-horse-battery') {
  fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: 'new-correct-horse-battery' } });
  fireEvent.change(screen.getByLabelText(/^Confirm new password/), { target: { value: confirm } });
}

describe('PRD-02 password recovery', () => {
  it.each([null, 'demo-token'])('uses the same confirmation for known and unknown accounts', async resetToken => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ accepted: true, resetToken }), { status: 202 })));
    render(<MemoryRouter><PasswordRecoveryPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'user@example.test' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Request password reset' }));
    await screen.findByText('Request received. Use a valid password reset link to continue.');
    expect(screen.queryByText('demo-token')).not.toBeInTheDocument();
  });
  it('preserves the email after a rate rejection and permits retry', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Too many requests. Please wait and retry.' }), { status: 429 }))
      .mockResolvedValueOnce(new Response('{"accepted":true}', { status: 202 })));
    render(<MemoryRouter><PasswordRecoveryPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'user@example.test' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Request password reset' }));
    await screen.findByText('Too many requests. Please wait and retry.');
    expect(screen.getByLabelText(/^Email/)).toHaveValue('user@example.test');
    fireEvent.submit(screen.getByRole('form', { name: 'Request password reset' }));
    await screen.findByRole('status');
  });
  it('keeps the fragment token out of history/DOM and sends it only in the mutation body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'a641aa83-4613-49ae-9f11-1138d91b1ac4', email: 'user@example.test', version: 2, emailVerified: true })));
    vi.stubGlobal('fetch', fetchMock);
    resetPage();
    await waitFor(() => expect(screen.getByLabelText('Location')).toHaveTextContent('/reset-password'));
    expect(screen.getByLabelText('Location')).not.toHaveTextContent('single-use-token');
    fillPasswords();
    fireEvent.submit(screen.getByRole('form', { name: 'Reset password' }));
    await screen.findByText('Password reset. Sign in with your new password.');
    expect(fetchMock.mock.calls[0][0]).toBe('/auth/password/reset');
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toMatchObject({ token: 'single-use-token', newPassword: 'new-correct-horse-battery' });
    expect(fetchMock.mock.calls[0][1].headers.get('X-StrataAI-Request')).toBe('1');
    expect(screen.queryByLabelText(/^New password/)).not.toBeInTheDocument();
  });
  it('rejects mismatched passwords before sending a request', () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    resetPage();
    fillPasswords('different-password');
    fireEvent.submit(screen.getByRole('form', { name: 'Reset password' }));
    expect(screen.getByText('Passwords must match.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it('requires a new link after an invalid or consumed token', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'The token is invalid or expired.', code: 'invalid_or_expired_token' }), { status: 400 })));
    resetPage();
    fillPasswords();
    fireEvent.submit(screen.getByRole('form', { name: 'Reset password' }));
    await screen.findByText('The token is invalid or expired.');
    expect(screen.queryByRole('button', { name: 'Reset password' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Request a new link' })).toBeInTheDocument();
  });
  it('does not accept reset tokens in the server-visible query string', () => {
    resetPage('/reset-password?token=query-token');
    expect(screen.getByText(/This reset link is missing/)).toBeInTheDocument();
    expect(screen.getByLabelText('Location')).not.toHaveTextContent('query-token');
  });
});
