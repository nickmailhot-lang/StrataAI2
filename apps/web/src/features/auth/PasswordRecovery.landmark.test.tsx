import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PasswordRecoveryPage } from './PasswordRecoveryPage';
import { ResetPasswordPage } from './ResetPasswordPage';
afterEach(() => vi.unstubAllGlobals());
const reset = (path = '/reset-password#token=synthetic-single-use') => render(<MemoryRouter initialEntries={[path]}><ResetPasswordPage /></MemoryRouter>);
const fill = () => {
  fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: 'synthetic-password-long-enough' } });
  fireEvent.change(screen.getByLabelText(/^Confirm new password/), { target: { value: 'synthetic-password-long-enough' } });
};
it('places the password-reset request form and return link inside one main landmark', () => {
  render(<MemoryRouter><PasswordRecoveryPage /></MemoryRouter>);
  const main = screen.getByRole('main'); expect(screen.getAllByRole('main')).toHaveLength(1);
  expect(within(main).getByRole('heading', { level: 1, name: 'Reset your password' })).toBeInTheDocument();
  expect(within(main).getByRole('form', { name: 'Request password reset' })).toBeInTheDocument();
  expect(within(main).getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login');
});
it('retains the enumeration-safe request outcome inside main', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response('{"accepted":true}', { status: 202 })); vi.stubGlobal('fetch', fetchMock);
  render(<MemoryRouter><PasswordRecoveryPage /></MemoryRouter>);
  fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'fixture@example.invalid' } });
  fireEvent.submit(screen.getByRole('form', { name: 'Request password reset' }));
  await screen.findByRole('status');
  expect(within(screen.getByRole('main')).getByRole('status')).toHaveTextContent('Request received. Use a valid password reset link to continue.');
  expect(fetchMock).toHaveBeenCalledOnce();
});
it('places the admitted reset form in main without rendering its fragment token', () => {
  reset(); const main = screen.getByRole('main'); expect(screen.getAllByRole('main')).toHaveLength(1);
  expect(within(main).getByRole('heading', { level: 1, name: 'Choose a new password' })).toBeInTheDocument();
  expect(within(main).getByRole('form', { name: 'Reset password' })).toBeInTheDocument();
  expect(main).not.toHaveTextContent('synthetic-single-use');
});
it('keeps missing-link guidance and recovery/return links in main without a request', () => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock); reset('/reset-password');
  const main = screen.getByRole('main');
  expect(within(main).getByRole('alert')).toHaveTextContent('This reset link is missing, invalid, or expired. Request a new link.');
  expect(within(main).getByRole('link', { name: 'Request a new link' })).toHaveAttribute('href', '/forgot-password');
  expect(within(main).getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login');
  expect(fetchMock).not.toHaveBeenCalled();
});
it('retains the acknowledged reset result inside main while clearing password controls', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'a641aa83-4613-49ae-9f11-1138d91b1ac4', email: 'fixture@example.invalid', version: 2, emailVerified: true }))); vi.stubGlobal('fetch', fetchMock);
  reset(); fill(); fireEvent.submit(screen.getByRole('form', { name: 'Reset password' }));
  await screen.findByRole('status');
  const main = screen.getByRole('main'); expect(within(main).getByRole('status')).toHaveTextContent('Password reset. Sign in with your new password.');
  expect(screen.queryByLabelText(/^New password/)).not.toBeInTheDocument(); expect(fetchMock).toHaveBeenCalledOnce();
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ token: 'synthetic-single-use', newPassword: 'synthetic-password-long-enough' });
});
