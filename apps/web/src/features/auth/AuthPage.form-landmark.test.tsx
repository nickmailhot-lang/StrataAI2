import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AuthPage } from './AuthPage';

afterEach(() => vi.unstubAllGlobals());
const page = () => render(<MemoryRouter><AuthPage /></MemoryRouter>);
const details = () => {
  fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'fixture@example.invalid' } });
  fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'synthetic-password-long-enough' } });
};
const register = () => {
  fireEvent.click(screen.getByRole('tab', { name: 'Register' }));
  fireEvent.change(screen.getByLabelText(/^Display name/), { target: { value: 'Fixture Person' } });
  details();
};

describe('PRD-02 active authentication form landmarks / UXA-06', () => {
  it('exposes one named sign-in form containing its existing fields and submit', () => {
    page(); const form = within(screen.getByRole('form', { name: 'Sign in' }));
    expect(screen.getAllByRole('form')).toHaveLength(1);
    expect(form.getByRole('textbox', { name: /^Email/ })).toBeInTheDocument();
    expect(form.getByLabelText(/^Password/)).toBeInTheDocument();
    expect(form.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });
  it('names only the active registration form and follows deliberate mode changes without losing drafts', () => {
    const request = vi.fn(); vi.stubGlobal('fetch', request); page(); register();
    expect(screen.queryByRole('form', { name: 'Sign in' })).not.toBeInTheDocument();
    const form = within(screen.getByRole('form', { name: 'Register' }));
    expect(form.getByRole('textbox', { name: /^Display name/ })).toHaveValue('Fixture Person');
    expect(form.getByRole('button', { name: 'Create account' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Sign in' }));
    expect(screen.queryByRole('form', { name: 'Register' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('form', { name: 'Sign in' })).getByRole('textbox', { name: /^Email/ })).toHaveValue('fixture@example.invalid');
    expect(request).not.toHaveBeenCalled();
  });
  it('retains its sign-in form name, draft and original command across rejected retries', async () => {
    const request = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ code: 'invalid_credentials' }), { status: 401 })));
    vi.stubGlobal('fetch', request); page(); details();
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText('The email or password is incorrect.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled());
    expect(request.mock.calls[1][1].body).toBe(request.mock.calls[0][1].body);
    expect(new Headers(request.mock.calls[1][1].headers).get('Idempotency-Key')).toBe(new Headers(request.mock.calls[0][1].headers).get('Idempotency-Key'));
    const form = within(screen.getByRole('form', { name: 'Sign in' }));
    expect(form.getByRole('textbox', { name: /^Email/ })).toHaveValue('fixture@example.invalid');
    expect(form.getByLabelText(/^Password/)).toHaveValue('synthetic-password-long-enough');
  });
  it('names the sign-in form after a validated registration acknowledgement clears the password', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ user: { id: '11111111-1111-4111-8111-111111111111', email: 'fixture@example.invalid', emailVerified: false }, verificationToken: 'synthetic-not-for-rendering' }), { status: 201 }));
    vi.stubGlobal('fetch', request); page(); register();
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText('Account created. Use your verification email to activate it before signing in.');
    expect(screen.queryByRole('form', { name: 'Register' })).not.toBeInTheDocument();
    const form = within(screen.getByRole('form', { name: 'Sign in' }));
    expect(form.getByRole('textbox', { name: /^Email/ })).toHaveValue('fixture@example.invalid');
    expect(form.getByLabelText(/^Password/)).toHaveValue('');
    expect(screen.queryByText('synthetic-not-for-rendering')).not.toBeInTheDocument();
    expect(request).toHaveBeenCalledOnce();
  });
  it('keeps the registration form named and drafts intact when email delivery is unavailable', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'identity_delivery_unavailable' }), { status: 503 }));
    vi.stubGlobal('fetch', request); page(); register();
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    await screen.findByText('Verification email is temporarily unavailable. Please retry later.');
    const form = within(screen.getByRole('form', { name: 'Register' }));
    expect(form.getByRole('textbox', { name: /^Display name/ })).toHaveValue('Fixture Person');
    expect(form.getByLabelText(/^Password/)).toHaveValue('synthetic-password-long-enough');
    expect(request).toHaveBeenCalledOnce();
  });
});
