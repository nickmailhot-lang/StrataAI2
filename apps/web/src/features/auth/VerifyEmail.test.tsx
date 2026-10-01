import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { VerifyEmailPage } from './VerifyEmailPage';

afterEach(() => vi.unstubAllGlobals());
function LocationProbe() { const location = useLocation(); return <output aria-label="Location">{location.pathname + location.search + location.hash}</output>; }
function page(path = '/verify-email') { render(<MemoryRouter initialEntries={[path]}><VerifyEmailPage /><LocationProbe /></MemoryRouter>); }

describe('PRD-02 verification delivery', () => {
  it('scrubs the fragment and requires explicit confirmation before consuming it', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'a641aa83-4613-49ae-9f11-1138d91b1ac4', email: 'user@example.test', version: 2, emailVerified: true })));
    vi.stubGlobal('fetch', request);
    page('/verify-email#token=verification-only-token');
    await waitFor(() => expect(screen.getByLabelText('Location')).not.toHaveTextContent('verification-only-token'));
    expect(request).not.toHaveBeenCalled();
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByText('Email verified. Sign in to continue.');
    expect(request.mock.calls[0][0]).toBe('/auth/verify-email');
    expect(JSON.parse(request.mock.calls[0][1].body)).toEqual({ token: 'verification-only-token' });
    expect(request.mock.calls[0][1].headers.get('X-StrataAI-Request')).toBe('1');
    expect(screen.queryByRole('button', { name: 'Verify email' })).not.toBeInTheDocument();
  });
  it.each([null, 'demo-verification-token'])('resend confirmation does not disclose account or token', async verificationToken => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ accepted: true, verificationToken }), { status: 202 })));
    page();
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'user@example.test' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByText('Request received. Use a valid verification link to continue.');
    expect(screen.queryByText('demo-verification-token')).not.toBeInTheDocument();
  });
  it('offers replacement after an expired token and preserves email on unavailable delivery', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Link expired.', code: 'invalid_or_expired_token' }), { status: 400 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Email temporarily unavailable.', code: 'identity_delivery_unavailable' }), { status: 503 })));
    page('/verify-email#token=expired');
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByText('The token is invalid or expired.');
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'user@example.test' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByText('Email is temporarily unavailable. Please retry later.');
    expect(screen.getByLabelText(/^Email/)).toHaveValue('user@example.test');
  });
  it('does not accept a token in the URL query', async () => {
    vi.stubGlobal('fetch', vi.fn());
    page('/verify-email?token=unsafe-query');
    await waitFor(() => expect(screen.getByLabelText('Location')).not.toHaveTextContent('unsafe-query'));
    expect(screen.getByLabelText(/^Email/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Verify email' })).not.toBeInTheDocument();
  });
});
