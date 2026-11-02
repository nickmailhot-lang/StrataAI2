import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { VerifyEmailPage } from './VerifyEmailPage';

afterEach(() => vi.unstubAllGlobals());
const page = (path = '/verify-email') => render(<MemoryRouter initialEntries={[path]}><VerifyEmailPage /></MemoryRouter>);
const main = () => {
  expect(screen.getAllByRole('main')).toHaveLength(1);
  return within(screen.getByRole('main'));
};

describe('PRD-02 verification landmarks / UXA-06', () => {
  it('contains the missing-link form, heading and sign-in return in one main', () => {
    const request = vi.fn(); vi.stubGlobal('fetch', request); page();
    expect(main().getByRole('heading', { level: 1, name: 'Verify your email' })).toBeInTheDocument();
    expect(main().getByRole('form', { name: 'Verify email' })).toBeInTheDocument();
    expect(main().getByRole('textbox', { name: /^Email/ })).toBeInTheDocument();
    expect(main().getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login');
    expect(request).not.toHaveBeenCalled();
  });
  it('contains deliberate token confirmation without rendering or automatically consuming the token', () => {
    const request = vi.fn(); vi.stubGlobal('fetch', request); page('/verify-email#token=synthetic-confirmation');
    expect(main().getByRole('button', { name: 'Verify email' })).toBeInTheDocument();
    expect(screen.getByRole('main')).not.toHaveTextContent('synthetic-confirmation');
    expect(request).not.toHaveBeenCalled();
  });
  it('contains a safe resend status without disclosing a returned token', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ accepted: true, verificationToken: 'synthetic-not-for-rendering' }), { status: 202 }));
    vi.stubGlobal('fetch', request); page();
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'fixture@example.invalid' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByRole('status');
    expect(main().getByRole('status')).toHaveTextContent('Request received. Use a valid verification link to continue.');
    expect(screen.getByRole('main')).not.toHaveTextContent('synthetic-not-for-rendering');
    expect(request).toHaveBeenCalledOnce();
    expect(JSON.parse(request.mock.calls[0][1].body)).toEqual({ email: 'fixture@example.invalid' });
  });
  it('contains the acknowledged verification outcome and return after clearing confirmation', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'a641aa83-4613-49ae-9f11-1138d91b1ac4', email: 'fixture@example.invalid', version: 2, emailVerified: true })));
    vi.stubGlobal('fetch', request); page('/verify-email#token=synthetic-confirmation');
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByRole('status');
    expect(main().getByRole('status')).toHaveTextContent('Email verified. Sign in to continue.');
    expect(main().queryByRole('button', { name: 'Verify email' })).not.toBeInTheDocument();
    expect(main().getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login');
    expect(request).toHaveBeenCalledOnce();
    expect(JSON.parse(request.mock.calls[0][1].body)).toEqual({ token: 'synthetic-confirmation' });
  });
  it('contains expired-link guidance and the replacement email field after refusal', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'invalid_or_expired_token' }), { status: 400 }));
    vi.stubGlobal('fetch', request); page('/verify-email#token=synthetic-expired');
    fireEvent.submit(screen.getByRole('form', { name: 'Verify email' }));
    await screen.findByRole('alert');
    expect(main().getByRole('alert')).toHaveTextContent('The token is invalid or expired.');
    expect(main().getByRole('textbox', { name: /^Email/ })).toBeInTheDocument();
    expect(request).toHaveBeenCalledOnce();
  });
});
