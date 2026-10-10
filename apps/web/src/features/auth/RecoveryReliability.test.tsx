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
    {
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


it.each(cases)('PRD-02: $name displays the actual safe failure reference and preserves its details', async testCase => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"title":"private-provider-detail"}', {
    status: 503, headers: { 'X-Correlation-ID': 'recovery.response-1' },
  })));
  mount(testCase);
  await screen.findByText('Reference: recovery.response-1');
  expect(screen.queryByText(/private-provider-detail/)).not.toBeInTheDocument();
  testCase.fields.forEach((field, i) => expect(screen.getByLabelText(new RegExp(`^${field}`))).toHaveValue(testCase.values[i]));
});


it.each(cases)('PRD-02: $name rejects malformed references and invents none after a network failure', async testCase => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'private:diagnostic' } }))
    .mockRejectedValueOnce(new Error('network diagnostic'));
  vi.stubGlobal('fetch', fetch); mount(testCase);
  await screen.findByRole('alert');
  expect(screen.queryByText(/Reference:|private:diagnostic/)).not.toBeInTheDocument();
  fireEvent.submit(screen.getByRole('form', { name: testCase.form }));
  await act(async () => { await Promise.resolve(); });
  expect(fetch).toHaveBeenCalledTimes(2);
  expect(screen.queryByText(/Reference:|network diagnostic/)).not.toBeInTheDocument();
});
it.each(cases)('PRD-02: $name retains the response reference if its success body cannot be read', async testCase => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: testCase.status, ok: true, headers: new Headers({ 'X-Correlation-ID': 'unreadable.response-1' }),
    json: () => Promise.reject(new Error('private body error')) }));
  mount(testCase); await screen.findByText('Reference: unreadable.response-1');
  expect(screen.queryByText(/private body error/)).not.toBeInTheDocument();
  expect(screen.queryByText(new RegExp(testCase.success))).not.toBeInTheDocument();
});
it.each(cases)('PRD-02: $name retires the old reference when retry starts and fences a late response after close', async testCase => {
  let complete: ((response: Response) => void) | undefined;
  const fetch = vi.fn().mockResolvedValueOnce(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'previous.response-1' } }))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { complete = resolve; }));
  vi.stubGlobal('fetch', fetch); const view = mount(testCase);
  await screen.findByText('Reference: previous.response-1');
  fireEvent.submit(screen.getByRole('form', { name: testCase.form }));
  expect(screen.queryByText(/previous.response-1/)).not.toBeInTheDocument();
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetch.mock.calls[0][1].headers.get('Idempotency-Key'));
  view.unmount();
  await act(async () => { complete?.(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'late.response-1' } })); });
  expect(screen.queryByText(/late.response-1/)).not.toBeInTheDocument();
});
