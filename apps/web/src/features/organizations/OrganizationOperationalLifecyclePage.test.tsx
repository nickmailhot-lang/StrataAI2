import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationOperationalLifecyclePage } from './OrganizationOperationalLifecyclePage';
import { readLifecycleIntent } from './operationalLifecycle';
import type { watchOperationalLifecycle } from './operationalLifecycleLive';
const live = vi.hoisted(() => ({ options: undefined as Parameters<typeof watchOperationalLifecycle>[0] | undefined }));
vi.mock('./operationalLifecycleLive', () => ({ watchOperationalLifecycle: (options: Parameters<typeof watchOperationalLifecycle>[0]) => {
  live.options = options; return () => {};
} }));
beforeEach(() => { live.options = undefined; });

it('reconciles a remote lifecycle snapshot through fresh admin review and cancels stale confirmation', async () => {
  let current = review;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : current)));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Review lifecycle change' }));
  await screen.findByRole('dialog'); current = { ...review, status: 'SUSPENDED', version: 4 };
  await act(async () => {
    await live.options!.admit({ organizationId: org, actorId: actor, status: 'SUSPENDED', version: 4,
      afterVersion: 0, nextVersion: null, pending: false, events: [] }, new AbortController().signal);
    live.options!.update({ organizationId: org, actorId: actor, status: 'SUSPENDED', version: 4,
      afterVersion: 0, nextVersion: null, pending: false, events: [] });
  });
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.getByText(/Revision 4/)).toBeInTheDocument(); expect(screen.getByRole('status')).toHaveTextContent('Organization lifecycle changed');
  expect(readLifecycleIntent(org)).toBeUndefined();
});
it('requires a fresh manual review after live interruption and keeps the critical flow accessible', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : review)));
  mount(); const launch = await screen.findByRole('button', { name: 'Review lifecycle change' });
  act(() => live.options!.unavailable()); expect(launch).toBeDisabled();
  expect(screen.getByRole('status')).toHaveTextContent('Live updates are unavailable');
  fireEvent.click(screen.getByRole('button', { name: 'Load current lifecycle' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review lifecycle change' })).toBeEnabled());
});
it('withdraws named review and dialog when the live consumer loses administration', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : review)));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Review lifecycle change' }));
  await screen.findByRole('dialog'); act(() => live.options!.denied(404, false));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByText('Reviewed Organization')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Review lifecycle change' })).not.toBeInTheDocument();
});
const actor = '22222222-2222-4222-8222-222222222222'; const org = '55555555-5555-4555-8555-555555555555';
const other = '33333333-3333-4333-8333-333333333333'; const event = '44444444-4444-4444-8444-444444444444';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const review = { organizationId: org, actorId: actor, status: 'ACTIVE', version: 3, updatedAt: '2026-10-10T12:00:00Z', actingCapacity: 'ORGANIZATION_OWNER', name: 'Reviewed Organization' };
const route = `/organizations/${org}/operational-lifecycle`;
function response(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status }); }
function acknowledgment(options: RequestInit) {
  const body = JSON.parse(options.body as string) as { status: string; version: number };
  return { organizationId: org, actorId: actor, eventId: event, actorLabel: 'Original caption', actingCapacity: 'ORGANIZATION_OWNER',
    previousStatus: 'ACTIVE', status: body.status, version: body.version+1, correlationId: 'original-lifecycle', createdAt: '2026-10-10T12:00:01Z' };
}
function mount(id = org) {
  const router = createMemoryRouter([{ path: '/app/:organizationId/operational-lifecycle', element: <OrganizationOperationalLifecyclePage /> },
    { path: '/app', element: <h1>Organizations destination</h1> }, { path: '/login', element: <h1>Sign in destination</h1> }],
    { initialEntries: [`/app/${id}/operational-lifecycle`] }); render(<RouterProvider router={router} />); return router;
}
async function confirm() { fireEvent.click(await screen.findByRole('button', { name: 'Review lifecycle change' })); fireEvent.click(screen.getByRole('button', { name: 'Confirm lifecycle change' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); sessionStorage.clear(); });

it('reviews explicit lifecycle and revision with keyboard-safe cancel and no mutation', async () => {
  const fetcher = vi.fn(async (path: string) => response(path === '/me' ? profile : review)); vi.stubGlobal('fetch', fetcher); mount();
  const launch = await screen.findByRole('button', { name: 'Review lifecycle change' }); expect(screen.getByText(/Revision 3/)).toBeInTheDocument();
  fireEvent.click(launch); const cancel = screen.getByRole('button', { name: 'Cancel lifecycle change' });
  await waitFor(() => expect(cancel).toHaveFocus()); fireEvent.click(cancel);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); await waitFor(() => expect(launch).toHaveFocus());
  expect(fetcher.mock.calls).toHaveLength(3);
});
it('confirms one bound revision/key and acknowledges original state without claiming latest state', async () => {
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile : options?.method === 'PATCH' ? acknowledgment(options) : review));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  const notice = await screen.findByRole('status'); expect(notice).toHaveTextContent('Original change acknowledged: Suspended, revision 4.');
  await waitFor(() => expect(notice).toHaveFocus()); const writes = fetcher.mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(writes).toHaveLength(1);
  expect(writes[0][0]).toBe(route); expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ status: 'SUSPENDED', version: 3 });
  const headers = writes[0][1]!.headers as Headers; expect(headers.get('X-StrataAI-Request')).toBe('1'); expect(headers.get('X-StrataAI-Expected-Actor')).toBe(actor);
  expect(headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/); expect(readLifecycleIntent(org)).toBeUndefined();
  expect(screen.queryByRole('button', { name: 'Review lifecycle change' })).not.toBeInTheDocument();
});
it('retains exact actor/state/revision/key through uncertainty and reload until original recovery', async () => {
  let count = 0; const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'PATCH') { if (++count < 3) throw new TypeError('Lost acknowledgment'); return response(acknowledgment(options)); }
    return response(review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  const retry = await screen.findByRole('button', { name: 'Retry original lifecycle change' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.getByRole('button', { name: 'Load current lifecycle' })).toBeDisabled();
  const original = readLifecycleIntent(org); expect(original?.status).toBe('SUSPENDED'); fireEvent.click(retry);
  await waitFor(() => expect(count).toBe(2)); await waitFor(() => expect(retry).toBeEnabled()); cleanup(); mount();
  const restored = await screen.findByRole('button', { name: 'Retry original lifecycle change' }); expect(count).toBe(2);
  fireEvent.click(restored); await screen.findByText(/Original change acknowledged/);
  const writes = fetcher.mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(writes).toHaveLength(3);
  expect(new Set(writes.map(([, options]) => options!.body)).size).toBe(1);
  expect(new Set(writes.map(([, options]) => (options!.headers as Headers).get('Idempotency-Key'))).size).toBe(1);
});
it.each([400, 409])('requires fresh review and confirmation after definitive refusal %s', async status => {
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile
    : options?.method === 'PATCH' ? { detail: 'Private database' } : review, options?.method === 'PATCH' ? status : 200));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText(/The lifecycle change was refused/);
  expect(readLifecycleIntent(org)).toBeUndefined(); expect(screen.queryByRole('button', { name: 'Retry original lifecycle change' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private database')).not.toBeInTheDocument(); const reload = await screen.findByRole('button', { name: 'Load current lifecycle' });
  await waitFor(() => expect(reload).toBeEnabled()); fireEvent.click(reload);
  await screen.findByRole('button', { name: 'Review lifecycle change' }); expect(fetcher.mock.calls.filter(([, options]) => options?.method === 'PATCH')).toHaveLength(1);
});
it.each([429, 500, 503])('preserves original request on unavailable acknowledgment %s', async status => {
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile
    : options?.method === 'PATCH' ? { detail: 'Private failure' } : review, options?.method === 'PATCH' ? status : 200)));
  mount(); await confirm(); await screen.findByRole('button', { name: 'Retry original lifecycle change' });
  expect(readLifecycleIntent(org)).toBeDefined(); expect(screen.queryByText('Private failure')).not.toBeInTheDocument();
});
it.each([403, 404])('withdraws review and recovery when administrative admission returns %s', async status => {
  let writes = 0; const fetcher = vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile
    : options?.method === 'PATCH' ? { detail: 'Private failure' } : review, options?.method === 'PATCH' ? (++writes === 1 ? 503 : status) : 200));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); fireEvent.click(await screen.findByRole('button', { name: 'Retry original lifecycle change' }));
  await screen.findByText('Organization lifecycle is unavailable to this account.'); expect(readLifecycleIntent(org)).toBeUndefined();
  expect(screen.queryByRole('button', { name: 'Retry original lifecycle change' })).not.toBeInTheDocument();
});
it('fences a switched account before mutation and never discloses an acknowledgment', async () => {
  let profiles = 0; const fetcher = vi.fn(async (path: string) => response(path === '/me' ? { ...profile, id: ++profiles > 2 ? other : actor } : review));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText('Sign in destination');
  expect(fetcher.mock.calls.filter(([path]) => path !== '/me')).toHaveLength(1); expect(readLifecycleIntent(org)).toBeUndefined();
});
it('refuses a changed revision discovered during confirmation without a stale mutation', async () => {
  let reviews = 0; const fetcher = vi.fn(async (path: string) => response(path === '/me' ? profile : { ...review, version: ++reviews > 1 ? 4 : 3 }));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText(/Organization changed elsewhere/);
  expect(fetcher.mock.calls.filter(([path]) => path === route)).toHaveLength(2); expect(readLifecycleIntent(org)).toBeUndefined();
});
it('requires a fresh review when the organization name changes during confirmation', async () => {
  let reviews = 0; const fetcher = vi.fn(async (path: string) => response(path === '/me' ? profile : { ...review, name: ++reviews > 1 ? 'Changed Organization' : review.name }));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm(); await screen.findByText(/Organization changed elsewhere/);
  expect(fetcher.mock.calls.filter(([path]) => path === route)).toHaveLength(2); expect(readLifecycleIntent(org)).toBeUndefined();
});
it.each(['SUSPENDED', 'DEACTIVATED'])('allows independent inactive review and explicit reactivation for %s', async status => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : { ...review, status })));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Review lifecycle change' }));
  expect(screen.getByRole('dialog')).toHaveTextContent('to Active'); expect(screen.getByRole('dialog')).toHaveTextContent('Eligible operational activity will resume.');
});
it('canonicalizes uppercase organization links before protected HTTP requests', async () => {
  const id = 'abcdefab-cdef-4abc-8def-abcdefabcdef'; const fetcher = vi.fn(async (path: string) => response(path === '/me' ? profile : { ...review, organizationId: id }));
  vi.stubGlobal('fetch', fetcher); mount(id.toUpperCase()); await screen.findByRole('button', { name: 'Review lifecycle change' });
  expect(fetcher.mock.calls[1][0]).toBe(`/organizations/${id}/operational-lifecycle`);
});
it('aborts pending IO and ignores late responses after leaving the route', async () => {
  let signal: AbortSignal | null | undefined;
  const fetcher = vi.fn((_path: string, options?: RequestInit) => { signal = options?.signal; return new Promise<Response>(() => {}); });
  vi.stubGlobal('fetch', fetcher); mount(); await waitFor(() => expect(fetcher).toHaveBeenCalled()); cleanup(); expect(signal?.aborted).toBe(true);
});
it('does not submit when reload recovery cannot be stored', async () => {
  const fetcher = vi.fn(async (path: string) => response(path === '/me' ? profile : review)); vi.stubGlobal('fetch', fetcher);
  mount(); await screen.findByRole('button', { name: 'Review lifecycle change' });
  const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Storage unavailable'); });
  await confirm(); await screen.findByText(/Unable to retain this request/); expect(fetcher.mock.calls).toHaveLength(6); storage.mockRestore();
});
it('withdraws an otherwise valid acknowledgment when current authority is lost after the write', async () => {
  let reviews = 0; const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'PATCH') return response(acknowledgment(options));
    return response(review, ++reviews === 3 ? 403 : 200);
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  await screen.findByText('Organization lifecycle is unavailable to this account.');
  expect(screen.queryByText(/Original change acknowledged/)).not.toBeInTheDocument(); expect(readLifecycleIntent(org)).toBeUndefined();
});
it('recovers an older receipt across a later reactivation without presenting it as current lifecycle', async () => {
  let writes = 0; const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'PATCH') { if (++writes === 1) throw new TypeError('Uncertain'); return response(acknowledgment(options)); }
    return response({ ...review, version: writes ? 6 : 3 });
  }); vi.stubGlobal('fetch', fetcher); mount(); await confirm(); fireEvent.click(await screen.findByRole('button', { name: 'Retry original lifecycle change' }));
  await screen.findByText(/Original change acknowledged: Suspended, revision 4/);
  const reload = await screen.findByRole('button', { name: 'Load current lifecycle' }); await waitFor(() => expect(reload).toBeEnabled()); fireEvent.click(reload);
  await screen.findByText(/Revision 6/); expect(writes).toBe(2);
});
