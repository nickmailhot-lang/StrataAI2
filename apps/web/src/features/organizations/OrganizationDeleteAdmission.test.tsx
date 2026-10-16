import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationDeletePage } from './OrganizationDeletePage';

const actor = '22222222-2222-4222-8222-222222222222';
const org = '55555555-5555-4555-8555-555555555555';
const cache = `strataai.organization-deletion.v1:${org}`;
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const review = { organization: { id: org, name: 'Private deletion Council', status: 0, version: 3 }, role: 0 };
const response = (body: unknown, status = 200) => new Response(status === 202 ? null : JSON.stringify(body), { status });
function mount() {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/delete', element: <OrganizationDeletePage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
  ], { initialEntries: [`/app/${org}/delete`] })} />);
}
async function open() { fireEvent.click(await screen.findByRole('button', { name: 'Review deletion request' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); sessionStorage.clear(); });

it.each(['switched', 401, 403, 404] as const)('refuses a deletion acknowledgment after final account admission changes (%s)', async admission => {
  let checks = 0; let writes = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me') {
      if (++checks === 4) return admission === 'switched' ? response({ ...profile, id: '33333333-3333-4333-8333-333333333333' }) : response({}, admission);
      return response(profile);
    }
    if (init?.method === 'DELETE') { writes++; return response(null, 202); }
    return response(review);
  }));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
  await screen.findByText(admission === 'switched' || admission === 401 ? 'Sign in destination' : 'Organization deletion is unavailable to this account.');
  expect(writes).toBe(1); expect(screen.queryByRole('status')).not.toBeInTheDocument();
  expect(screen.queryByText('Private deletion Council')).not.toBeInTheDocument();
  if (admission === 401) expect(JSON.parse(sessionStorage.getItem(cache)!).acknowledged).toBe(false);
  else expect(sessionStorage.getItem(cache)).toBeNull();
});

it('bounds the whole initial account/Organization review and ignores late private metadata after the deadline', async () => {
  let releaseProfile!: (value: Response) => void; let releaseReview!: (value: Response) => void;
  let readSignal: AbortSignal | undefined; let checks = 0;
  vi.stubGlobal('fetch', vi.fn((path: string, init?: RequestInit) => {
    if (path === '/me') { checks++; return new Promise<Response>(done => { releaseProfile = done; }); }
    readSignal = init?.signal as AbortSignal; return new Promise<Response>(done => { releaseReview = done; });
  }));
  vi.useFakeTimers(); mount();
  await act(async () => { await vi.advanceTimersByTimeAsync(0); });
  await act(async () => { await vi.advanceTimersByTimeAsync(8_000); releaseProfile(response(profile)); });
  expect(releaseReview).toBeDefined();
  await act(async () => { await vi.advanceTimersByTimeAsync(7_001); }); vi.useRealTimers();
  await screen.findByText('Unable to review current deletion permission. Try loading it again.');
  expect(readSignal?.aborted).toBe(true);
  await act(async () => { releaseReview(response(review)); });
  expect(checks).toBe(1); expect(screen.queryByText('Private deletion Council')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Review current deletion permission' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: 'Review deletion request' })).not.toBeInTheDocument();
});

it('shares one deadline across the pre-write account and DELETE, then recovers only the original request', async () => {
  let checks = 0; let releaseProfile!: (value: Response) => void; let releaseWrite!: (value: Response) => void;
  let writeSignal: AbortSignal | undefined; const writes: RequestInit[] = [];
  vi.stubGlobal('fetch', vi.fn((path: string, init?: RequestInit) => {
    if (path === '/me') return ++checks === 3 ? new Promise<Response>(done => { releaseProfile = done; }) : Promise.resolve(response(profile));
    if (init?.method === 'DELETE') {
      writes.push(init); writeSignal = init.signal as AbortSignal;
      return writes.length === 1 ? new Promise<Response>(done => { releaseWrite = done; }) : Promise.resolve(response(null, 202));
    }
    return Promise.resolve(response(review));
  }));
  mount(); await open(); vi.useFakeTimers();
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' })); await vi.advanceTimersByTimeAsync(0); });
  await act(async () => { await vi.advanceTimersByTimeAsync(8_000); releaseProfile(response(profile)); });
  expect(writes).toHaveLength(1);
  await act(async () => { await vi.advanceTimersByTimeAsync(7_001); });
  await act(async () => { await vi.advanceTimersByTimeAsync(300); }); vi.useRealTimers();
  const retry = await screen.findByRole('button', { name: 'Retry original deletion request' }); expect(retry).toBeEnabled();
  expect(writeSignal?.aborted).toBe(true); expect(screen.queryByText('Private deletion Council')).not.toBeInTheDocument();
  await act(async () => { releaseWrite(response(null, 202)); }); expect(screen.queryByRole('status')).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByText('Deletion request acknowledged. Deletion has not been confirmed complete.');
  expect(writes).toHaveLength(2);
  expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  expect(JSON.parse(sessionStorage.getItem(cache)!).acknowledged).toBe(true);
});

it('bounds account plus status observation as one operation and ignores a late terminal snapshot', async () => {
  let checking = false; let checks = 0; let key = ''; let statusReads = 0;
  let releaseProfile!: (value: Response) => void; let releaseStatus!: (value: Response) => void; let statusSignal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn((path: string, init?: RequestInit) => {
    if (path === '/me') return checking && ++checks === 1 ? new Promise<Response>(done => { releaseProfile = done; }) : Promise.resolve(response(profile));
    if (init?.method === 'DELETE') { key = new Headers(init.headers).get('Idempotency-Key')!; return Promise.resolve(response(null, 202)); }
    if (path.includes('/deletion-requests/')) { statusReads++; statusSignal = init?.signal as AbortSignal; return new Promise<Response>(done => { releaseStatus = done; }); }
    return Promise.resolve(response(review));
  }));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
  const check = await screen.findByRole('button', { name: 'Check deletion status' }); await waitFor(() => expect(check).toBeEnabled());
  checking = true; vi.useFakeTimers();
  await act(async () => { fireEvent.click(check); await vi.advanceTimersByTimeAsync(0); });
  await act(async () => { await vi.advanceTimersByTimeAsync(8_000); releaseProfile(response(profile)); });
  await act(async () => { await vi.advanceTimersByTimeAsync(7_001); }); vi.useRealTimers();
  await screen.findByText('Unable to confirm deletion status. Check the same request again.'); expect(check).toBeEnabled(); expect(statusSignal?.aborted).toBe(true);
  await act(async () => { releaseStatus(response({ requestId: key, state: 'COMPLETED', version: 5, eventId: '77777777-7777-4777-8777-777777777777', completedAt: '2026-10-06T18:00:00Z' })); });
  expect(screen.queryByText('Organization deletion confirmed complete.')).not.toBeInTheDocument();
  expect(statusReads).toBe(1); expect(checks).toBe(1); expect(JSON.parse(sessionStorage.getItem(cache)!).key).toBe(key);
});

it('keeps a committed request unacknowledged when final account verification is unavailable, then recovers the same key', async () => {
  let checks = 0; const writes: RequestInit[] = [];
  vi.stubGlobal('fetch', vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me') return ++checks === 4 ? response({}, 503) : response(profile);
    if (init?.method === 'DELETE') { writes.push(init); return response(null, 202); }
    return response(review);
  }));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Confirm deletion request' }));
  const retry = await screen.findByRole('button', { name: 'Retry original deletion request' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByRole('status')).not.toBeInTheDocument(); expect(JSON.parse(sessionStorage.getItem(cache)!).acknowledged).toBe(false);
  fireEvent.click(retry); await screen.findByText('Deletion request acknowledged. Deletion has not been confirmed complete.');
  expect(writes).toHaveLength(2); expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
});
