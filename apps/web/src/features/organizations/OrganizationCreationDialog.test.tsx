import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationCreationDialog } from './OrganizationCreationDialog';
const actor = '22222222-2222-4222-8222-222222222222';
const org = '55555555-5555-4555-8555-555555555555';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const original = { organization: { id: org, name: 'Original Organization', description: '', status: 0, version: 1, ownerUserId: actor, type: 'STRATA' }, role: 0 };
const current = { organization: { ...original.organization, name: 'Later Organization', version: 2 }, role: 2 };
function response(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status }); }
function mount() {
  const onCreated = vi.fn(); const onCancel = vi.fn();
  const router = createMemoryRouter([{ path: '/', element: <OrganizationCreationDialog actorId={actor} onCreated={onCreated} onCancel={onCancel} /> },
    { path: '/login', element: <h1>Sign in destination</h1> }, { path: '/away', element: <h1>Other destination</h1> }], { initialEntries: ['/'] });
  render(<RouterProvider router={router} />); return { router, onCreated, onCancel };
}
function create() {
  fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Original Organization' } });
  fireEvent.click(screen.getByRole('button', { name: /^Create$/ }));
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

it('submits the Strata default as part of the immutable creation intent', async () => {
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'POST' ? response({ ...original, organization: { ...original.organization, type: 'STRATA' } }, 201)
      : response({ ...current, organization: { ...current.organization, type: 'STRATA' } }));
  vi.stubGlobal('fetch', fetcher); const view = mount(); create();
  await waitFor(() => expect(view.onCreated).toHaveBeenCalledWith(org));
  const writes = fetcher.mock.calls.filter(([, options]) => options?.method === 'POST');
  expect(writes).toHaveLength(1);
  expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ name: 'Original Organization', description: '', type: 'STRATA' });
});

it('retains the selected type and original body/key after an uncertain creation', async () => {
  let writes = 0;
  const typed = { ...original, organization: { ...original.organization, type: 'HOA' } };
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') { if (++writes === 1) throw new TypeError('Lost acknowledgment'); return response(typed, 201); }
    return response(typed);
  });
  vi.stubGlobal('fetch', fetcher); const view = mount();
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Organization type' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Homeowners association' })); create();
  const retry = await screen.findByRole('button', { name: 'Retry original creation' });
  expect(screen.getByRole('combobox', { name: 'Organization type' })).toHaveAttribute('aria-disabled', 'true');
  fireEvent.click(retry); await waitFor(() => expect(view.onCreated).toHaveBeenCalledWith(org));
  const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'POST');
  expect(calls).toHaveLength(2); expect(calls[0][1]!.body).toBe(calls[1][1]!.body);
  expect(JSON.parse(calls[0][1]!.body as string).type).toBe('HOA');
  expect((calls[0][1]!.headers as Headers).get('Idempotency-Key')).toBe((calls[1][1]!.headers as Headers).get('Idempotency-Key'));
});

it.each([undefined, 'UNKNOWN', 'STRATA'])('withholds creation navigation for a mismatched type acknowledgment (%s)', async acknowledgedType => {
  let writes = 0;
  const typed = { ...original, organization: { ...original.organization, type: 'HOA' } };
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') return response(++writes === 1
      ? { ...original, organization: { ...original.organization, type: acknowledgedType } } : typed, 201);
    return response(typed);
  });
  vi.stubGlobal('fetch', fetcher); const view = mount();
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Organization type' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Homeowners association' })); create();
  const retry = await screen.findByRole('button', { name: 'Retry original creation' });
  expect(view.onCreated).not.toHaveBeenCalled();
  fireEvent.click(retry); await waitFor(() => expect(view.onCreated).toHaveBeenCalledWith(org));
  const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'POST');
  expect(calls).toHaveLength(2); expect(calls[0][1]!.body).toBe(calls[1][1]!.body);
  expect(JSON.parse(calls[0][1]!.body as string).type).toBe('HOA');
  expect((calls[0][1]!.headers as Headers).get('Idempotency-Key')).toBe((calls[1][1]!.headers as Headers).get('Idempotency-Key'));
});

it('retries the immutable original account/body/key and checks later canonical membership before opening', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') { if (++writes === 1) throw new TypeError('Lost acknowledgment'); return response(original, 201); }
    return response(current);
  });
  vi.stubGlobal('fetch', fetcher); const view = mount(); create();
  const retry = await screen.findByRole('button', { name: 'Retry original creation' });
  await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.getByRole('textbox', { name: 'Name' })).toBeDisabled(); expect(screen.getByLabelText('Description')).toBeDisabled();
  expect(view.onCreated).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'Return to Organizations' })).toBeDisabled(); fireEvent.click(retry);
  await waitFor(() => expect(view.onCreated).toHaveBeenCalledWith(org));
  const calls = fetcher.mock.calls.filter(([, options]) => options?.method === 'POST');
  expect(calls).toHaveLength(2); expect(calls[0][0]).toBe(`/organizations?expectedActorId=${actor}`);
  expect(calls[1][0]).toBe(calls[0][0]); expect(calls[1][1]!.body).toBe(calls[0][1]!.body);
  expect((calls[1][1]!.headers as Headers).get('Idempotency-Key')).toBe((calls[0][1]!.headers as Headers).get('Idempotency-Key'));
  expect(fetcher.mock.calls.filter(([path]) => path === `/organizations/${org}`)).toHaveLength(1);
  expect(screen.queryByText('Original Organization')).not.toBeInTheDocument();
});

it('keeps the same intent when current access cannot be read after a real creation acknowledgment', async () => {
  let reads = 0;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'POST' ? response(original, 201) : ++reads === 1 ? response({}, 503) : response(current));
  vi.stubGlobal('fetch', fetcher); const view = mount(); create();
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original creation' }));
  await waitFor(() => expect(view.onCreated).toHaveBeenCalledWith(org));
  const keys = fetcher.mock.calls.filter(([, options]) => options?.method === 'POST').map(([, options]) => (options!.headers as Headers).get('Idempotency-Key'));
  expect(keys).toHaveLength(2); expect(keys[0]).toBe(keys[1]);
});

it.each([401, 403, 404])('clears private input and refuses recovery after current access returns %s', async status => {
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'POST' ? response(original, 201) : response({ detail: 'Private SQL identifier' }, status)));
  const view = mount(); create();
  await screen.findByText(status === 401 ? 'Sign in destination' : /creation or current access is unavailable/);
  expect(view.onCreated).not.toHaveBeenCalled(); expect(screen.queryByDisplayValue('Original Organization')).not.toBeInTheDocument();
  expect(screen.queryByText('Private SQL identifier')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original creation' })).not.toBeInTheDocument();
});

it('rejects a switched account before sending the original creation', async () => {
  const fetcher = vi.fn(async () => response({ ...profile, id: '33333333-3333-4333-8333-333333333333' }));
  vi.stubGlobal('fetch', fetcher); const view = mount(); create();
  await screen.findByText('Sign in destination'); expect(view.onCreated).not.toHaveBeenCalled();
  expect(fetcher.mock.calls).toHaveLength(1);
});

it('rejects a switched account after current admission without opening the Organization', async () => {
  let accounts = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => path === '/me'
    ? response({ ...profile, id: ++accounts === 1 ? actor : '33333333-3333-4333-8333-333333333333' })
    : response(options?.method === 'POST' ? original : current, options?.method === 'POST' ? 201 : 200)));
  const view = mount(); create(); await screen.findByText('Sign in destination'); expect(view.onCreated).not.toHaveBeenCalled();
});

it.each([400, 409, 429])('requires directory review after definitive refusal %s without inventing another creation', async status => {
  const fetcher = vi.fn(async (path: string) => path === '/me' ? response(profile) : response({ detail: 'Do not disclose' }, status));
  vi.stubGlobal('fetch', fetcher); const view = mount(); create();
  await screen.findByText(/Return to the directory and check current Organizations/);
  expect(screen.queryByRole('button', { name: /^Create$/ })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original creation' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Return to Organizations' })); expect(view.onCancel).toHaveBeenCalledOnce();
  expect(view.onCreated).not.toHaveBeenCalled(); expect(fetcher.mock.calls).toHaveLength(2);
});

it('does not accept a foreign original receipt or duplicate-create after malformed success', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? response(profile)
    : response({ ...original, organization: { ...original.organization, ownerUserId: '33333333-3333-4333-8333-333333333333' } }, 201)));
  const view = mount(); create(); await screen.findByRole('button', { name: 'Retry original creation' });
  expect(view.onCreated).not.toHaveBeenCalled(); expect(screen.getByRole('textbox', { name: 'Name' })).toBeDisabled();
});

it('aborts route departure and ignores a late creation acknowledgment', async () => {
  let resolve!: (value: Response) => void; let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn((path: string, options?: RequestInit) => path === '/me' ? Promise.resolve(response(profile))
    : new Promise<Response>(done => { resolve = done; signal = options?.signal as AbortSignal; })));
  const view = mount(); create(); await waitFor(() => expect(resolve).toBeDefined());
  await act(() => view.router.navigate('/away')); expect(signal?.aborted).toBe(true);
  await act(async () => { resolve(response(original, 201)); });
  expect(view.onCreated).not.toHaveBeenCalled(); expect(screen.getByText('Other destination')).toBeVisible();
});

it('bounds an ignored transport and keeps the original intent while fencing its late result', async () => {
  let resolve!: (value: Response) => void; let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn((path: string, options?: RequestInit) => path === '/me' ? Promise.resolve(response(profile))
    : new Promise<Response>(done => { resolve = done; signal = options?.signal as AbortSignal; })));
  vi.useFakeTimers(); const view = mount();
  await act(async () => { create(); await vi.advanceTimersByTimeAsync(0); });
  expect(resolve).toBeDefined();
  await act(async () => { await vi.advanceTimersByTimeAsync(15_001); }); vi.useRealTimers();
  expect(signal?.aborted).toBe(true); await screen.findByRole('button', { name: 'Retry original creation' });
  await act(async () => { resolve(response(original, 201)); }); expect(view.onCreated).not.toHaveBeenCalled();
});

it('retains the same key through repeated uncertainty and refuses a switched retry account', async () => {
  let accounts = 0; let writes = 0;
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => {
    if (path === '/me') return response({ ...profile, id: ++accounts < 3 ? actor : '33333333-3333-4333-8333-333333333333' });
    writes++; return response({}, 503);
  });
  vi.stubGlobal('fetch', fetcher); const view = mount(); create();
  const retry = await screen.findByRole('button', { name: 'Retry original creation' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await waitFor(() => expect(writes).toBe(2)); await waitFor(() => expect(retry).toBeEnabled());
  const keys = fetcher.mock.calls.filter(([path]) => path !== '/me').map(([, options]) => (options!.headers as Headers).get('Idempotency-Key'));
  expect(keys).toHaveLength(2); expect(keys[1]).toBe(keys[0]);
  fireEvent.click(retry); await screen.findByText('Sign in destination'); expect(writes).toBe(2); expect(view.onCreated).not.toHaveBeenCalled();
});

it('bounds the complete creation including late canonical JSON and recovers the same original request', async () => {
  let checks = 0; let finishBody!: (value: unknown) => void; let signal!: AbortSignal;
  const posts: RequestInit[] = [];
  const fetcher = vi.fn((path: string, options: RequestInit = {}) => {
    if (path === '/me') {
      if (++checks === 1) return new Promise<Response>(resolve => setTimeout(() => resolve(response(profile)), 8000));
      return Promise.resolve(response(profile));
    }
    if (options.method === 'POST') { posts.push(options); return Promise.resolve(response(original, 201)); }
    if (posts.length === 1) {
      signal = options.signal!; const canonical = response(current);
      canonical.json = () => new Promise<unknown>(resolve => { finishBody = resolve; }); return Promise.resolve(canonical);
    }
    return Promise.resolve(response(current));
  });
  vi.stubGlobal('fetch', fetcher); vi.useFakeTimers(); const view = mount();
  await act(async () => create()); await act(async () => vi.advanceTimersByTimeAsync(8000));
  expect(posts).toHaveLength(1); expect(finishBody).toBeDefined();
  await act(async () => vi.advanceTimersByTimeAsync(7001)); expect(signal.aborted).toBe(true);
  expect(screen.getByRole('button', { name: 'Retry original creation' })).toBeEnabled();
  expect(screen.getByRole('textbox', { name: 'Name' })).toBeDisabled();
  expect(view.onCreated).not.toHaveBeenCalled();
  await act(async () => finishBody(current)); expect(view.onCreated).not.toHaveBeenCalled();
  vi.useRealTimers(); fireEvent.click(screen.getByRole('button', { name: 'Retry original creation' }));
  await waitFor(() => expect(view.onCreated).toHaveBeenCalledWith(org));
  expect(posts).toHaveLength(2); expect(posts[1].body).toBe(posts[0].body);
  expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'));
});
it('bounds an unsent account check, keeps its reserved request and describes no creation as sent', async () => {
  let finishBody!: (value: unknown) => void; let signal!: AbortSignal;
  const fetcher = vi.fn((_path: string, options: RequestInit = {}) => {
    signal = options.signal!; const me = response(profile);
    me.json = () => new Promise<unknown>(resolve => { finishBody = resolve; }); return Promise.resolve(me);
  });
  vi.stubGlobal('fetch', fetcher); vi.useFakeTimers(); const view = mount();
  await act(async () => create()); await act(async () => vi.advanceTimersByTimeAsync(15001));
  expect(signal.aborted).toBe(true); expect(screen.getByText(/No creation was sent/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Retry original creation' })).toBeEnabled();
  await act(async () => finishBody(profile)); expect(view.onCreated).not.toHaveBeenCalled();
  expect(fetcher.mock.calls).toHaveLength(1);
});
