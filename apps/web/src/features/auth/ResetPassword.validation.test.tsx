import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ResetPasswordPage } from './ResetPasswordPage';

afterEach(() => vi.unstubAllGlobals());
const guidance = 'Enter the same password in both fields.';
const page = () => render(<MemoryRouter initialEntries={['/reset-password#token=synthetic-reset-proof']}><ResetPasswordPage /></MemoryRouter>);
const fill = (confirmation = 'synthetic-password-second') => {
  fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: 'synthetic-password-first' } });
  fireEvent.change(screen.getByLabelText(/^Confirm new password/), { target: { value: confirmation } });
};
const submit = () => fireEvent.submit(screen.getByRole('form', { name: 'Reset password' }));

describe('PRD-02 password confirmation field feedback / UXA-06/07', () => {
  it('does not mark an unsubmitted mismatch invalid or issue a request while typing', () => {
    const request = vi.fn(); vi.stubGlobal('fetch', request); page(); fill();
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText(guidance)).not.toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
  });
  it('associates local mismatch guidance with the confirmation field while retaining the original alert and zero-request guard', () => {
    const request = vi.fn(); vi.stubGlobal('fetch', request); page(); fill(); submit();
    expect(screen.getByRole('alert')).toHaveTextContent('Passwords must match.');
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveAccessibleDescription(guidance);
    expect(screen.getByLabelText(/^New password/)).toHaveAttribute('aria-invalid', 'false');
    expect(request).not.toHaveBeenCalled();
  });
  it.each(['New password', 'Confirm new password'])('clears stale confirmation field feedback when correcting %s without submitting or clearing drafts', label => {
    const request = vi.fn(); vi.stubGlobal('fetch', request); page(); fill(); submit();
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveAttribute('aria-invalid', 'true');
    const value = label === 'New password' ? 'synthetic-password-second' : 'synthetic-password-first';
    fireEvent.change(screen.getByLabelText(new RegExp('^' + label)), { target: { value } });
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText(guidance)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^New password/)).toHaveValue(value);
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveValue(value);
    expect(request).not.toHaveBeenCalled();
  });
  it('does not attribute a storage refusal to confirmation or discard matching drafts', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'identity_storage_unavailable' }), { status: 503 }));
    vi.stubGlobal('fetch', request); page(); fill('synthetic-password-first'); submit();
    await screen.findByRole('alert');
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText(guidance)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^New password/)).toHaveValue('synthetic-password-first');
    expect(screen.getByLabelText(/^Confirm new password/)).toHaveValue('synthetic-password-first');
    expect(request).toHaveBeenCalledOnce();
  });
});
