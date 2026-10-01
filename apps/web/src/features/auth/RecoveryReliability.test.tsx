import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PasswordRecoveryPage } from './PasswordRecoveryPage';
import { ResetPasswordPage } from './ResetPasswordPage';
import { VerifyEmailPage } from './VerifyEmailPage';

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
const cases = [
  { name: 'password request', path: '/forgot-password', Page: PasswordRecoveryPage, form: 'Request password reset', fields: ['Email'], values: ['user@example.test'], success: 'Request received.', status: 202 },
  { name: 'password reset', path: '/reset-password#token=private-reset-token', Page: ResetPasswordPage, form: 'Reset password', fields: ['New password', 'Confirm new password'], values: ['new-correct-horse-battery', 'new-correct-horse-battery'], success: 'Password reset. Sign in', status: 200 },
  { name: 'verification', path: '/verify-email#token=private-verify-token', Page: VerifyEmailPage, form: 'Verify email', fields: [], values: [], success: 'Email verified.', status: 200 },
  { name: 'verification resend', path: '/verify-email', Page: VerifyEmailPage, form: 'Verify email', fields: ['Email'], values: ['user@example.test'], success: 'Request received.', status: 202 },
];
function mount(testCase: typeof cases[number]) {
  const { Page } = testCase;
  const view = render(<MemoryRouter initialEntries={[testCase.path]}><Page /></MemoryRouter>);
  testCase.fields.forEach((field, i) => fireEvent.change(screen.getByLabelText(new RegExp(`^${field}`)), { target: { value: testCase.values[i] } }));
  fireEvent.submit(screen.getByRole('form', { name: testCase.form }));
  return view;
}
describe('PRD-02-TC-06/PRD-60-TC-07 recovery acknowledgment safety', () => {
  it.each(cases)('$name preserves recoverable details after a malformed success', async testCase => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: testCase.status })));
    mount(testCase);
    await screen.findByRole('alert');
    expect(screen.queryByText(new RegExp(testCase.success))).not.toBeInTheDocument();
    testCase.fields.forEach((field, i) => expect(screen.getByLabelText(new RegExp(`^${field}`))).toHaveValue(testCase.values[i]));
    expect(screen.getByRole('form', { name: testCase.form })).toHaveAttribute('aria-busy', 'false');
  });
  it.each(cases)('$name bounds a stalled response body and permits another request', async testCase => {
    vi.useFakeTimers();
    const fetchMock = vi.fn().mockResolvedValue({ status: testCase.status, json: () => new Promise(() => {}) });
    vi.stubGlobal('fetch', fetchMock);
    mount(testCase);
    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
    expect(screen.getByRole('alert')).toHaveTextContent(/could not be confirmed/);
    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(true);
    testCase.fields.forEach((field, i) => expect(screen.getByLabelText(new RegExp(`^${field}`))).toHaveValue(testCase.values[i]));
    fireEvent.submit(screen.getByRole('form', { name: testCase.form }));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    if (testCase.status === 202) {
      const firstKey = fetchMock.mock.calls[0][1].headers.get('Idempotency-Key');
      expect(firstKey).toMatch(/^[0-9a-f-]{36}$/);
      expect(fetchMock.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(firstKey);
    }
  });
  it.each(cases)('$name cancels its transport when the screen closes', async testCase => {
    const fetchMock = vi.fn().mockImplementation(() => new Promise(() => {}));
    vi.stubGlobal('fetch', fetchMock);
    const view = mount(testCase);
    view.unmount();
    expect(fetchMock.mock.calls[0][1].signal.aborted).toBe(true);
  });
  it.each(cases)('$name never displays an untrusted error title', async testCase => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"title":"private-provider-password-details"}', { status: 503 })));
    mount(testCase);
    await screen.findByRole('alert');
    expect(screen.queryByText(/private-provider-password-details/)).not.toBeInTheDocument();
  });
});
