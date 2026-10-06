import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationDeletePage } from './OrganizationDeletePage';
const actor = '22222222-2222-4222-8222-222222222222';
const org = '55555555-5555-4555-8555-555555555555';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const review = { organization: { id: org, name: 'Private Council', status: 0, version: 3 }, role: 0 };
function response(body: unknown, status = 200) { return status === 202 ? new Response(null, { status }) : new Response(JSON.stringify(body), { status }); }
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/delete', element: <OrganizationDeletePage /> },
    { path: '/app', element: <h1>Organizations destination</h1> }, { path: '/login', element: <h1>Sign in destination</h1> }],
    { initialEntries: [`/app/${org}/delete`] }); render(<RouterProvider router={router} />); return router;
}
async function confirm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Review deletion request' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

it('requires explicit keyboard-safe confirmation and cancels without a deletion request', async () => {
  const fetcher = vi.fn(async (path: string) => response(path === '/me' ? profile : review)); vi.stubGlobal('fetch', fetcher); mount();
  const opener = await screen.findByRole('button', { name: 'Review deletion request' }); fireEvent.click(opener);
  const cancel = screen.getByRole('button', { name: 'Cancel deletion request' }); await waitFor(() => expect(cancel).toHaveFocus());
  fireEvent.click(cancel); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(opener).toHaveFocus()); expect(fetcher.mock.calls).toHaveLength(3);
});

it('acknowledges the bound reviewed request without claiming completed deletion', async () => {
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile : review, options?.method === 'DELETE' ? 202 : 200));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  const notice = await screen.findByRole('status'); expect(notice).toHaveTextContent('Deletion request acknowledged. Deletion has not been confirmed complete.');
  await waitFor(() => expect(notice).toHaveFocus()); expect(screen.queryByText('Private Council')).not.toBeInTheDocument();
  const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'DELETE'); expect(calls).toHaveLength(1);
  expect(calls[0][0]).toBe(`/organizations/${org}?version=3&expectedActorId=${actor}`);
  expect((calls[0][1]!.headers as Headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(screen.queryByRole('button', { name: 'Review deletion request' })).not.toBeInTheDocument();
});

it('retains exact actor/version/key through repeated uncertainty without a new review or write intent', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'DELETE') { if (++writes < 3) throw new TypeError('Lost acknowledgment'); return response(null, 202); }
    return response(review);
  });
  vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  const retry = await screen.findByRole('button', { name: 'Retry original deletion request' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.getByRole('button', { name: 'Review current deletion permission' })).toBeDisabled();
  expect(screen.queryByText('Private Council')).not.toBeInTheDocument(); fireEvent.click(retry);
  await waitFor(() => expect(writes).toBe(2)); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  const notice = await screen.findByRole('status'); await waitFor(() => expect(notice).toHaveFocus());
  const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'DELETE'); expect(calls).toHaveLength(3);
  expect(new Set(calls.map(([path]) => path)).size).toBe(1);
  expect(new Set(calls.map(([, options]) => (options!.headers as Headers).get('Idempotency-Key'))).size).toBe(1);
  expect(fetcher.mock.calls.filter(([path]) => path === `/organizations/${org}`)).toHaveLength(1);
});

it.each([1, 2])('does not expose deletion confirmation to Organization role %s', async role => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : { ...review, role })));
  mount(); await screen.findByText('Only a current Organization Owner can request deletion.');
  expect(screen.queryByRole('button', { name: 'Review deletion request' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private Council')).not.toBeInTheDocument();
});

it.each([400, 409, 429])('requires a fresh review and confirmation after definitive refusal %s', async status => {
  let version = 3; const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'DELETE') { version = 4; return response({ detail: 'Private SQL' }, status); }
    return response({ ...review, organization: { ...review.organization, version } });
  });
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText(/The deletion request was refused/);
  expect(screen.queryByRole('button', { name: 'Retry original deletion request' })).not.toBeInTheDocument();
  const reload = await screen.findByRole('button', { name: 'Review current deletion permission' }); await waitFor(() => expect(reload).toBeEnabled()); fireEvent.click(reload);
  await screen.findByRole('button', { name: 'Review deletion request' });
  expect(fetcher.mock.calls.filter(([, options]) => options?.method === 'DELETE')).toHaveLength(1); expect(screen.queryByText('Private SQL')).not.toBeInTheDocument();
});

it.each([401, 403, 404])('clears private review and original intent when recovery returns %s', async status => {
  let writes = 0; vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'DELETE' ? response({ detail: 'Do not expose private failure' }, ++writes === 1 ? 503 : status) : response(review)));
  mount(); await confirm(); const retry = await screen.findByRole('button', { name: 'Retry original deletion request' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization deletion is unavailable to this account.');
  expect(screen.queryByText('Private Council')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Retry original deletion request' })).not.toBeInTheDocument();
  expect(screen.queryByText('Do not expose private failure')).not.toBeInTheDocument(); expect(screen.queryByRole('status')).not.toBeInTheDocument();
});

it('fences an account switch during canonical deletion review', async () => {
  let accounts = 0; const fetcher = vi.fn(async (path: string) => response(path === '/me'
    ? { ...profile, id: ++accounts === 1 ? actor : '33333333-3333-4333-8333-333333333333' } : review));
  vi.stubGlobal('fetch', fetcher); mount(); await screen.findByText('Sign in destination');
  expect(screen.queryByText('Private Council')).not.toBeInTheDocument(); expect(fetcher.mock.calls).toHaveLength(3);
});

it('fences a changed account before resending the original deletion', async () => {
  let accounts = 0; let writes = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => path === '/me'
    ? response({ ...profile, id: ++accounts <= 3 ? actor : '33333333-3333-4333-8333-333333333333' })
    : options?.method === 'DELETE' ? (writes++, response({}, 503)) : response(review)));
  mount(); await confirm(); const retry = await screen.findByRole('button', { name: 'Retry original deletion request' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry); await screen.findByText('Sign in destination'); expect(writes).toBe(1);
});

it('aborts a route departure and ignores a late deletion acknowledgment', async () => {
  let resolve!: (response: Response) => void; let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn((path: string, options?: RequestInit) => options?.method === 'DELETE'
    ? new Promise<Response>(done => { resolve = done; signal = options.signal as AbortSignal; }) : Promise.resolve(response(path === '/me' ? profile : review))));
  const router = mount(); await confirm(); await waitFor(() => expect(resolve).toBeDefined());
  await act(() => router.navigate('/app')); expect(signal?.aborted).toBe(true);
  await act(async () => { resolve(response(null, 202)); }); expect(screen.getByText('Organizations destination')).toBeVisible(); expect(screen.queryByRole('status')).not.toBeInTheDocument();
});

it('bounds an ignored transport and keeps the original reviewed request after its deadline', async () => {
  let resolve!: (response: Response) => void; let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn((path: string, options?: RequestInit) => options?.method === 'DELETE'
    ? new Promise<Response>(done => { resolve = done; signal = options.signal as AbortSignal; }) : Promise.resolve(response(path === '/me' ? profile : review))));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Review deletion request' }));
  vi.useFakeTimers(); await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' })); await vi.advanceTimersByTimeAsync(0); });
  expect(resolve).toBeDefined(); await act(async () => { await vi.advanceTimersByTimeAsync(15_001); });
  await act(async () => { await vi.advanceTimersByTimeAsync(300); }); vi.useRealTimers();
  expect(signal?.aborted).toBe(true); await screen.findByRole('button', { name: 'Retry original deletion request' });
  await act(async () => { resolve(response(null, 202)); }); expect(screen.queryByRole('status')).not.toBeInTheDocument();
});

it('uses a newly reviewed version and new key only after renewed explicit confirmation', async () => {
  let writes = 0; let version = 3;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'DELETE') {
      if (++writes === 1) { version = 4; return response({}, 409); }
      return response(null, 202);
    }
    return response({ ...review, organization: { ...review.organization, version } });
  });
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText(/The deletion request was refused/);
  fireEvent.click(await screen.findByRole('button', { name: 'Review current deletion permission' }));
  const launcher = await screen.findByRole('button', { name: 'Review deletion request' }); expect(writes).toBe(1);
  fireEvent.click(launcher); expect(writes).toBe(1); fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
  await screen.findByRole('status'); const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'DELETE');
  expect(calls).toHaveLength(2); expect(calls[0][0]).toContain('?version=3&'); expect(calls[1][0]).toContain('?version=4&');
  expect((calls[1][1]!.headers as Headers).get('Idempotency-Key')).not.toBe((calls[0][1]!.headers as Headers).get('Idempotency-Key'));
});
