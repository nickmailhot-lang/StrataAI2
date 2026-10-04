import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { NotificationCenterPage } from './NotificationCenterPage';
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-1111-1111-111111111111', recipient = '22222222-2222-2222-2222-222222222222';
const board = '44444444-4444-4444-4444-444444444444', card = '55555555-5555-5555-5555-555555555555';
const id = (n: number) => `66666666-6666-6666-6666-${String(n).padStart(12, '0')}`;
const profile = { id: recipient, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const item = (n = 1) => ({ id: id(n), actorId: board, recipientId: recipient, boardId: board, entityId: card,
  type: 'CARD_ASSIGNED', entityType: 'Card', createdAt: '2026-10-02T10:00:00.000001Z', readAt: null as string | null,
  entityLink: `/app/${org}/boards/${board}/cards/${card}` });
const data = (items = [item()], nextCursor: string | null = null) => ({ organizationId: org, items, nextCursor });
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function mount() { return render(<MemoryRouter initialEntries={[`/app/${org}/notifications`]}><Routes>
  <Route path="/app/:organizationId/notifications" element={<NotificationCenterPage />} />
</Routes></MemoryRouter>); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

it.each([['CARD_MOVED', 'Card moved'], ['LABEL_REMOVED', 'Label removed']])('renders %s activity with the existing accessible read and Card-link controls', async (type, label) => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data([{ ...item(), type }]))).mockResolvedValueOnce(response(profile));
  vi.stubGlobal('fetch', fetch); mount();
  expect(await screen.findByText(`${label} · Unread`)).toBeVisible();
  expect(screen.getByRole('link', { name: 'Open Card' })).toHaveAttribute('href', item().entityLink);
  expect(screen.getByRole('button', { name: 'Mark read' })).toBeEnabled();
  expect(screen.getByRole('article').getAttribute('aria-label')).toMatch(new RegExp(`^${label},`));
  expect(screen.getByRole('checkbox').getAttribute('aria-label')).toMatch(new RegExp(`^Select unread ${label.toLowerCase()} from`));
});

it('renders recipient-only Card links and sends explicit selected IDs with a retry key', async () => {
  const readAt = '2026-10-02T11:00:00Z';
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data([item(2), item(1)]))).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ organizationId: org, items: [1, 2].map(n => ({ id: id(n), readAt })) }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data([2, 1].map(n => ({ ...item(n), readAt }))))).mockResolvedValueOnce(response(profile));
  vi.stubGlobal('fetch', fetch); mount(); await screen.findByText('2 unread on this page.');
  expect(screen.getAllByRole('link', { name: 'Open Card' })[0]).toHaveAttribute('href', item().entityLink);
  fireEvent.click(screen.getByRole('button', { name: 'Select unread on this page' }));
  fireEvent.click(screen.getByRole('button', { name: 'Mark selected read' })); await screen.findByText('0 unread on this page.');
  const options = fetch.mock.calls[4][1]; expect(fetch.mock.calls[4][0]).toBe(`/organizations/${org}/notifications/read`);
  expect(options.method).toBe('POST'); expect(JSON.parse(options.body)).toEqual({ ids: [id(1), id(2)] });
  expect(new Headers(options.headers).get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh notifications' })).toHaveFocus());
});
it('retries the same original selection after a lost response and a newer canonical page', async () => {
  let read = false, attempts = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') {
      read = true; if (++attempts === 1) throw new Error('lost response');
      return response({ organizationId: org, items: [{ id: id(1), readAt: '2026-10-02T11:00:00Z' }] });
    }
    return response(read ? data([item(2)]) : data());
  });
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  await screen.findByRole('button', { name: 'Retry mark read' });
  fireEvent.click(screen.getByRole('button', { name: 'Refresh notifications' })); await screen.findByText('1 unread on this page.');
  expect(screen.getByRole('button', { name: 'Mark read' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Retry mark read' })); await waitFor(() => expect(attempts).toBe(2));
  const commands = fetch.mock.calls.filter(call => call[1]?.method === 'POST');
  expect(commands[1][1]?.body).toBe(commands[0][1]?.body);
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});
it.each([401, 403, 404])('clears private data and retry intent when access is denied (%s)', async status => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ detail: 'private raw error' }, status));
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  await screen.findByRole('button', { name: 'Check notifications again' });
  expect(screen.queryByRole('region', { name: 'Notification inbox' })).not.toBeInTheDocument();
  expect(screen.queryByText('private raw error')).not.toBeInTheDocument(); expect(screen.queryByText('Retry mark read')).not.toBeInTheDocument();
});
it('checks the fresh account before submitting an old recipient intent', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response({ ...profile, id: board }));
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  await screen.findByText('Your account changed. Check notifications again.'); expect(fetch).toHaveBeenCalledTimes(4);
  expect(screen.queryByRole('region', { name: 'Notification inbox' })).not.toBeInTheDocument();
});
it('keeps original recovery focus through a fresh inbox read and respects navigation away', async () => {
  let resolve!: (value: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockRejectedValueOnce(new Error('lost'))
    .mockResolvedValueOnce(response(profile)).mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; })).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile));
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  const retry = await screen.findByRole('button', { name: 'Retry mark read' }); await waitFor(() => expect(retry).toHaveFocus());
  fireEvent(window, new Event('focus')); await waitFor(() => expect(resolve).toBeDefined());
  expect(retry).toBeDisabled(); retry.blur(); expect(screen.queryByRole('region', { name: 'Notification inbox' })).not.toBeInTheDocument();
  await act(async () => resolve(response(data()))); await waitFor(() => expect(retry).toHaveFocus());
  const navigation = screen.getByRole('link', { name: 'Open boards' }); navigation.focus();
  fireEvent(window, new Event('focus')); await screen.findByRole('region', { name: 'Notification inbox' });
  expect(navigation).toHaveFocus(); expect(fetch.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
});
it('preserves returned refresh focus through later automatic inbox reads', async () => {
  let resolve!: (value: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ organizationId: org, items: [{ id: id(1), readAt: '2026-10-02T11:00:00Z' }] }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data([{ ...item(), readAt: '2026-10-02T11:00:00Z' }]))).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockImplementationOnce(() => new Promise<Response>(done => { resolve = done; })).mockResolvedValueOnce(response(profile));
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  const refresh = screen.getByRole('button', { name: 'Refresh notifications' }); await waitFor(() => expect(refresh).toHaveFocus());
  fireEvent(window, new Event('focus')); await waitFor(() => expect(resolve).toBeDefined()); expect(refresh).toBeDisabled(); refresh.blur();
  await act(async () => resolve(response(data([{ ...item(), readAt: '2026-10-02T11:00:00Z' }]))));
  await waitFor(() => expect(refresh).toHaveFocus()); expect(fetch.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
});
it('retains refresh intent when a completed read focuses an already focused control and later disablement blurs to body', async () => {
  let read = false;
  const fetch = vi.fn().mockImplementation(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') { read = true; return response({ organizationId: org, items: [{ id: id(1), readAt: '2026-10-02T11:00:00Z' }] }); }
    return response(data([{ ...item(), readAt: read ? '2026-10-02T11:00:00Z' : null }]));
  });
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  await screen.findByText('0 unread on this page.');
  const refresh = screen.getByRole('button', { name: 'Refresh notifications' }); await waitFor(() => expect(refresh).toHaveFocus());
  fireEvent(window, new Event('focus')); await waitFor(() => expect(fetch).toHaveBeenCalledTimes(11));
  await screen.findByText('0 unread on this page.'); await waitFor(() => expect(refresh).toHaveFocus());
  act(() => refresh.blur()); expect(document.body).toHaveFocus();
  fireEvent(window, new Event('focus')); await waitFor(() => expect(fetch).toHaveBeenCalledTimes(14));
  await waitFor(() => expect(refresh).toHaveFocus());
  expect(fetch.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
});
it('replaces fifty-item pages and clears the selection at a seek boundary', async () => {
  const items = Array.from({ length: 50 }, (_, n) => item(51 - n)); const cursor = `${items[49].createdAt}/${items[49].id}`;
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data(items, cursor))).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile));
  vi.stubGlobal('fetch', fetch); mount(); await screen.findByText('50 unread on this page.');
  fireEvent.click(screen.getByRole('button', { name: 'Select unread on this page' })); fireEvent.click(screen.getByRole('button', { name: 'Next notifications' }));
  await screen.findByText('1 unread on this page.'); expect(screen.getAllByRole('article')).toHaveLength(1);
  expect(screen.getByRole('button', { name: 'Mark selected read' })).toBeDisabled();
  expect(fetch.mock.calls[4][0]).toBe(`/organizations/${org}/notifications?after=${encodeURIComponent(cursor)}`);
});
it('refreshes on focus and fences a late response after unmount', async () => {
  let resolve!: (value: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockImplementationOnce(() => new Promise(r => { resolve = r; }));
  vi.stubGlobal('fetch', fetch); const view = mount(); await screen.findByText('1 unread on this page.');
  fireEvent(window, new Event('focus')); await waitFor(() => expect(resolve).toBeDefined()); view.unmount();
  await act(async () => resolve(response(data()))); expect(screen.queryByRole('region', { name: 'Notification inbox' })).not.toBeInTheDocument();
  expect(fetch.mock.calls[4][1].signal.aborted).toBe(true);
});
it('automatically recovers a newly assigned Card through the periodic HTTP read', async () => {
  vi.useFakeTimers();
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data([]))).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile));
  vi.stubGlobal('fetch', fetch);
  await act(async () => { mount(); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.getByText('0 unread on this page.')).toBeInTheDocument();
  await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
  expect(screen.getByText('1 unread on this page.')).toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(6);
});
it.each([400, 409])('retires a rejected selection and requires canonical refresh (%s)', async status => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data())).mockResolvedValueOnce(response(profile))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ detail: 'raw rejected selection' }, status));
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Mark read' }));
  await screen.findByText('The selection changed. Refresh notifications before trying again.');
  expect(screen.queryByText('Retry mark read')).not.toBeInTheDocument(); expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByText('raw rejected selection')).not.toBeInTheDocument();
});

it('automatically re-admits the inbox when connectivity returns after a failed read', async () => {
  let online = true, available = false;
  const fetch = vi.fn(async (path: string) => {
    if (!online) throw new Error('private network diagnostic');
    return response(path === '/me' ? profile : data(available ? [item()] : []));
  });
  vi.stubGlobal('fetch', fetch); const view = mount();
  await screen.findByText('0 unread on this page.');
  online = false;
  fireEvent.click(screen.getByRole('button', { name: 'Refresh notifications' }));
  await screen.findByText('Unable to load current notifications. Try again.');
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByText('private network diagnostic')).not.toBeInTheDocument();
  available = true; online = true; fireEvent(window, new Event('online'));
  await screen.findByText('1 unread on this page.');
  expect(screen.getByRole('article', { name: /^Assigned to you,/ })).toBeVisible();
  expect(screen.getByRole('checkbox', { name: /^Select unread assigned to you from/ })).toBeEnabled();
  view.unmount(); const calls = fetch.mock.calls.length;
  fireEvent(window, new Event('online')); expect(fetch).toHaveBeenCalledTimes(calls);
});
it.each(['changed', 'malformed', 'unavailable'] as const)('withholds the entire inbox until post-read account admission succeeds (%s)', async kind => {
  let profileReads = 0, admit!: (value: Response) => void;
  const fetch = vi.fn(async (path: string) => {
    if (path !== '/me') return response(data());
    if (++profileReads === 1) return response(profile);
    return new Promise<Response>(resolve => { admit = resolve; });
  });
  vi.stubGlobal('fetch', fetch); mount(); await waitFor(() => expect(admit).toBeDefined());
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Open Card' })).not.toBeInTheDocument();
  await act(async () => admit(kind === 'changed' ? response({ ...profile, id: board }) :
    kind === 'malformed' ? response({ ...profile, status: 'DEACTIVATED' }) : response({ detail: 'private admission diagnostic' }, 503)));
  await screen.findByText(kind === 'changed' ? 'Your account changed. Check notifications again.' : 'Unable to load current notifications. Try again.');
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Mark read' })).not.toBeInTheDocument();
  expect(screen.queryByText('private admission diagnostic')).not.toBeInTheDocument();
  expect(fetch.mock.calls.map(call => call[0])).toEqual(['/me', `/organizations/${org}/notifications`, '/me']);
});