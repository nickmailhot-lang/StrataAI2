import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ComponentProps } from 'react';
import { WatchControl } from './WatchControl';
const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222';
const entity = '33333333-3333-3333-3333-333333333333', user = '44444444-4444-4444-4444-444444444444', subscription = '55555555-5555-5555-5555-555555555555';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const props = { organizationId: org, boardId: board, entityId: entity, entityType: 'CARD' as const, admitted: true, disabled: false };
const empty = { organizationId: org, boardId: board, userId: user, entityType: 'CARD', entityId: entity, watching: false,
  version: 0, subscriptionId: null, createdAt: null, updatedAt: null, changed: false, canChange: true };
const changed = { ...empty, watching: true, version: 1, subscriptionId: subscription, createdAt: '2026-10-02T12:00:00Z', updatedAt: '2026-10-02T12:00:00Z', changed: true };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function mount(p: ComponentProps<typeof WatchControl> = props) { return render(<MemoryRouter><WatchControl {...p} /></MemoryRouter>); }
async function open(kind = 'Card') { fireEvent.click(screen.getByRole('button', { name: `${kind} watching` })); await screen.findByText(`You are not watching this ${kind}.`); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

it('permits a freshly authorized read during Board refresh while keeping commands disabled until refresh finishes', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty));
  vi.stubGlobal('fetch', fetch); const view = mount({ ...props, refreshing: true });
  expect(screen.getByRole('button', { name: 'Card watching' })).toBeEnabled(); await open();
  expect(screen.getByRole('button', { name: 'Watch Card' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Done watching' })).toBeEnabled(); expect(fetch).toHaveBeenCalledTimes(2);
  view.rerender(<MemoryRouter><WatchControl {...props} refreshing={false} /></MemoryRouter>);
  expect(screen.getByRole('button', { name: 'Watch Card' })).toBeEnabled(); expect(fetch).toHaveBeenCalledTimes(2);
});

it('dismisses a pending read-only check, aborts it and preserves a fresh reopen without applying the late result', async () => {
  let finish!: (value: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty));
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Check current watching' }));
  await waitFor(() => expect(finish).toBeDefined()); const signal = fetch.mock.calls[2][1]?.signal as AbortSignal;
  expect(screen.getByRole('button', { name: 'Done watching' })).toBeEnabled(); fireEvent.click(screen.getByRole('button', { name: 'Done watching' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(signal.aborted).toBe(true);
  await act(async () => finish(response(profile))); expect(fetch).toHaveBeenCalledTimes(3);
  await open(); expect(fetch).toHaveBeenCalledTimes(5);
});

it.each(['CARD', 'LIST', 'BOARD'] as const)('loads %s state on demand, submits its revision and reconciles canonical state', async type => {
  const kind = type === 'CARD' ? 'Card' : type === 'LIST' ? 'List' : 'Board';
  const target = type === 'BOARD' ? board : entity;
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...empty, entityType: type, entityId: target }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, entityType: type, entityId: target }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, entityType: type, entityId: target, changed: false }));
  vi.stubGlobal('fetch', fetch); mount({ ...props, entityType: type, entityId: target }); expect(fetch).not.toHaveBeenCalled(); await open(kind);
  fireEvent.click(screen.getByRole('button', { name: `Watch ${kind}` })); await screen.findByText(`You are watching this ${kind}.`);
  expect(fetch.mock.calls[3][0]).toBe(`/watch/${type}/${target}?version=0`); expect(fetch.mock.calls[3][1].method).toBe('PUT');
  expect(new Headers(fetch.mock.calls[3][1].headers).get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/);
  fireEvent.click(screen.getByRole('button', { name: 'Done watching' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument(), { timeout: 5_000 });
  await waitFor(() => expect(screen.getByRole('button', { name: `${kind} watching` })).toHaveFocus());
});
it('keeps the same recipient, revision, method and retry key after a lost response and newer canonical state', async () => {
  let attempts = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'PUT') { if (++attempts === 1) throw new Error('lost'); return response(changed); }
    return response(attempts ? { ...changed, version: 2, watching: false, changed: false } : empty);
  });
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Watch Card' }));
  await screen.findByRole('button', { name: 'Retry same watch change' }); expect(screen.getByRole('button', { name: 'Done watching' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Check current watching' })); await screen.findByText('You are not watching this Card.');
  fireEvent.click(screen.getByRole('button', { name: 'Retry same watch change' })); await waitFor(() => expect(attempts).toBe(2));
  await waitFor(() => expect(screen.queryByText('Retry same watch change')).not.toBeInTheDocument());
  const commands = fetch.mock.calls.filter(call => call[1]?.method === 'PUT'); expect(commands[1][0]).toBe(commands[0][0]);
  expect(commands[1][1]?.body).toBe(commands[0][1]?.body);
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});
it('unwatches using DELETE at the latest personal revision', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, changed: false }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, watching: false, version: 2 }))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, watching: false, version: 2, changed: false }));
  vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(screen.getByRole('button', { name: 'Card watching' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Unwatch Card' })); await screen.findByText('You are not watching this Card.');
  expect(fetch.mock.calls[3][0]).toBe(`/watch/CARD/${entity}?version=1`); expect(fetch.mock.calls[3][1].method).toBe('DELETE');
});
it.each([401, 403, 404])('clears denied personal state without exposing server content (%s)', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ detail: 'raw private detail' }, status)));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Watch Card' })); await screen.findByText('Watching is unavailable. Check access or sign in.');
  expect(screen.queryByText('You are not watching this Card.')).not.toBeInTheDocument(); expect(screen.queryByText('Retry same watch change')).not.toBeInTheDocument();
  expect(screen.queryByText('raw private detail')).not.toBeInTheDocument();
});
it('retires an old account intent before any write under the new account', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty)).mockResolvedValueOnce(response({ ...profile, id: board }));
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Watch Card' }));
  await screen.findByText('Your account changed. Check watching again.'); expect(fetch).toHaveBeenCalledTimes(3);
});
it.each([{ ...empty, userId: board }, { ...empty, entityId: board }, { ...empty, organizationId: board },
  { ...empty, watching: true }, { ...changed, changed: false, version: 0 }, { ...changed, changed: false, subscriptionId: null }])('rejects malformed or cross-scope personal state (%j)', async data => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(data)));
  mount(); fireEvent.click(screen.getByRole('button', { name: 'Card watching' })); await screen.findByText('Unable to check current watching. Try again.');
  expect(screen.queryByRole('button', { name: 'Watch Card' })).not.toBeInTheDocument();
});
it('disables mutation for the server-admitted archived Organization read state', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...empty, canChange: false })));
  mount(); await open(); expect(screen.getByRole('button', { name: 'Watch Card' })).toBeDisabled();
  expect(screen.getByText('Watching is read-only while this Organization is archived.')).toBeInTheDocument();
});
it('fences a late mutation after entity permission loss', async () => {
  let resolve!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty))
    .mockResolvedValueOnce(response(profile)).mockImplementationOnce(() => new Promise(r => { resolve = r; })));
  const view = mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Watch Card' })); await waitFor(() => expect(resolve).toBeDefined());
  view.rerender(<MemoryRouter><WatchControl {...props} admitted={false} /></MemoryRouter>);
  await act(async () => resolve(response(changed))); expect(screen.queryByText('Retry same watch change')).not.toBeInTheDocument();
  expect(screen.getByText('Watching is unavailable for this entity.')).toBeInTheDocument();
});
it.each([400, 409])('retires a rejected revision and requires a fresh canonical check (%s)', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ detail: 'raw conflict detail' }, status)));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Watch Card' }));
  await screen.findByText('Watching changed. Check the current state before trying again.');
  expect(screen.queryByText('Retry same watch change')).not.toBeInTheDocument(); expect(screen.queryByText('raw conflict detail')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Watch Card' })).not.toBeInTheDocument();
});
it('requires recovery when a mutation acknowledgment has the wrong revision', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, version: 2 })));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Watch Card' }));
  await screen.findByRole('button', { name: 'Retry same watch change' });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry same watch change' })).toHaveFocus());
  expect(screen.getByRole('button', { name: 'Done watching' })).toBeDisabled();
});
it('automatically recovers another client watch change while the dialog remains open', async () => {
  vi.useFakeTimers();
  const fetch = vi.fn().mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response(empty))
    .mockResolvedValueOnce(response(profile)).mockResolvedValueOnce(response({ ...changed, changed: false }));
  vi.stubGlobal('fetch', fetch);
  mount();
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Card watching' })); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.getByText('You are not watching this Card.')).toBeInTheDocument();
  await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
  expect(screen.getByText('You are watching this Card.')).toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(4);
});
