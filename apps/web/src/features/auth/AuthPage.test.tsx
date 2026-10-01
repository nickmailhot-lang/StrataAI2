import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

import { AuthPage } from './AuthPage';

describe('PRD-02 authentication UI', () => {
  afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

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
