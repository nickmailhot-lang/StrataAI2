import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BoardStarControl } from './BoardStarControl';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
import { watchBoardStars } from './boardStarLive';
vi.mock('./boardStarLive', () => ({ watchBoardStars: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222';
const user = '33333333-3333-3333-3333-333333333333', other = '44444444-4444-4444-4444-444444444444';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const props = { organizationId: org, boardId: board, admitted: true, disabled: false };
const state = { organizationId: org, boardId: board, userId: user, starred: false, version: 0, createdAt: null, updatedAt: null };
const retained = { ...state, version: 1, createdAt: '2026-10-04T12:00:00Z', updatedAt: '2026-10-04T12:00:00Z' };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
async function open() {
  fireEvent.click(screen.getByRole('button', { name: 'Board starring' }));
  await screen.findByText('You have not starred this Board.');
}
it.each(['Star Board', 'Done'])('restores keyboard-owned %s after a private background read interrupts it', async name => {
  let hold = false; let complete!: (value: Response) => void;
  const pending = new Promise<Response>(resolve => { complete = resolve; });
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? response(profile)
    : hold ? pending : response(state)));
  render(<BoardStarControl {...props} />); await open();
  const action = screen.getByRole('button', { name });
  await waitFor(() => expect(action).toBeEnabled()); action.focus(); expect(action).toHaveFocus();
  hold = true; act(() => vi.mocked(watchBoardStars).mock.calls.at(-1)![0].invalidate());
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Star Board' })).toBeNull());
  expect(screen.getByRole('dialog')).toHaveFocus();
  await act(async () => { complete(response(state)); });
  await waitFor(() => expect(screen.getByRole('button', { name })).toBeEnabled());
  await waitFor(() => expect(screen.getByRole('button', { name })).toHaveFocus());
});
it('refreshes private events without restarting the actor stream and stops on access withdrawal', async () => {
  const stop = vi.fn(); vi.mocked(watchBoardStars).mockClear(); vi.mocked(watchBoardStars).mockReturnValueOnce(stop);
  let starred = false;
  const fetch = vi.fn(async (path: string) => path === '/me' ? response(profile) : response(starred ? { ...retained, starred: true } : state));
  vi.stubGlobal('fetch', fetch); const view = render(<BoardStarControl {...props} />); await open();
  await waitFor(() => expect(watchBoardStars).toHaveBeenCalledTimes(1));
  const options = vi.mocked(watchBoardStars).mock.calls[0][0];
  expect(options).toMatchObject({ organizationId: org, boardId: board, userId: user });
  starred = true; act(() => options.invalidate());
  await screen.findByText('You have starred this Board.');
  expect(watchBoardStars).toHaveBeenCalledTimes(1);
  view.rerender(<BoardStarControl {...props} admitted={false} />);
  await waitFor(() => expect(stop).toHaveBeenCalledTimes(1));
  expect(screen.queryByText('You have starred this Board.')).not.toBeInTheDocument();
});
it('closes a keyboard-owned Done during a background read and ignores its late private response', async () => {
  let hold = false; let complete!: (value: Response) => void; let readSignal: AbortSignal | undefined;
  const pending = new Promise<Response>(resolve => { complete = resolve; });
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (hold && path.endsWith('/star')) { readSignal = options?.signal as AbortSignal; return pending; }
    return response(state);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  const done = screen.getByRole('button', { name: 'Done' });
  await waitFor(() => expect(done).toBeEnabled()); done.focus(); expect(done).toHaveFocus();
  hold = true; act(() => vi.mocked(watchBoardStars).mock.calls.at(-1)![0].invalidate());
  await waitFor(() => expect(readSignal).toBeDefined());
  const dialog = screen.getByRole('dialog'); expect(dialog).toHaveFocus(); expect(done).toBeEnabled();
  fireEvent.keyDown(dialog, { key: 'Enter' });
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(readSignal!.aborted).toBe(true);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Board starring' })).toHaveFocus());
  await act(async () => { complete(response({ ...retained, starred: true })); });
  expect(screen.queryByText('You have starred this Board.')).not.toBeInTheDocument();
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(fetch.mock.calls.some(([, options]) => ['PUT', 'DELETE'].includes(options?.method ?? ''))).toBe(false);
});
it('ends private event delivery when the current account changes during refresh', async () => {
  const stop = vi.fn(); vi.mocked(watchBoardStars).mockClear(); vi.mocked(watchBoardStars).mockReturnValueOnce(stop);
  let changed = false;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? response({ ...profile, id: changed ? other : user }) : response(state)));
  render(<BoardStarControl {...props} />); await open();
  await waitFor(() => expect(watchBoardStars).toHaveBeenCalledTimes(1));
  changed = true; act(() => vi.mocked(watchBoardStars).mock.calls[0][0].invalidate());
  await screen.findByText('Your account changed. Close and reopen Board starring.');
  // Rendering the notice does not itself flush the subscription effect cleanup.
  await waitFor(() => expect(stop).toHaveBeenCalledTimes(1));
  expect(watchBoardStars).toHaveBeenCalledTimes(1);
  expect(screen.queryByText('You have not starred this Board.')).not.toBeInTheDocument();
});
it('reads current personal state after acknowledgment rather than assuming an old replay is current', async () => {
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'PUT' ? new Response(null, { status: 204 }) : response(state));
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  await waitFor(() => expect(fetch.mock.calls.filter(([, options]) => options?.method === 'PUT')).toHaveLength(1));
  await screen.findByText('You have not starred this Board.');
  expect(screen.queryByText('You have starred this Board.')).not.toBeInTheDocument();
  const write = fetch.mock.calls.find(([, options]) => options?.method === 'PUT')!;
  expect(new Headers(write[1]?.headers).get('X-StrataAI-Request')).toBe('1');
  expect(new Headers(write[1]?.headers).get('Idempotency-Key')).toBeTruthy();
});
it('retains the original operation and key through an unknown result and a newer preference', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'PUT') { if (++writes === 1) throw new Error('Private response diagnostic'); return new Response(null, { status: 204 }); }
    return response(writes > 0 ? { ...retained, starred: true } : state);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  const retry = await screen.findByRole('button', { name: 'Retry same star change' });
  await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByRole('button', { name: 'Done' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByRole('button', { name: 'Unstar Board' });
  const commands = fetch.mock.calls.filter(([, options]) => options?.method === 'PUT');
  expect(commands).toHaveLength(2);
  expect(commands[0][0]).toBe('/boards/' + board + '/star?version=0');
  expect(commands[1][0]).toBe(commands[0][0]);
  expect(new Headers(commands[0][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1]?.headers).get('Idempotency-Key'));
  expect(screen.queryByText('Private response diagnostic')).not.toBeInTheDocument();
});
it('checks the current account before sending a mutation and retires a different account', async () => {
  let changed = false;
  const fetch = vi.fn(async (path: string) => path === '/me' ? response({ ...profile, id: changed ? other : user }) : response(state));
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open(); changed = true;
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  await screen.findByText('Your account changed. Close and reopen Board starring.');
  expect(fetch.mock.calls.every(call => call[0] === '/me' || call[0] === '/boards/' + board + '/star')).toBe(true);
  expect(fetch.mock.calls).toHaveLength(4);
  expect(screen.queryByRole('button', { name: 'Retry same star change' })).not.toBeInTheDocument();
});
it.each(['actor', 'scope', 'extra', 'identity'])('withholds malformed or cross-account preference reads (%s)', async kind => {
  let profiles = 0;
  const fetch = vi.fn(async (path: string) => path === '/me' ? response({ ...profile, id: kind === 'identity' && ++profiles > 1 ? other : user })
    : response(kind === 'actor' ? { ...state, userId: other } : kind === 'scope' ? { ...state, boardId: other }
      : kind === 'extra' ? { ...state, privateContent: 'secret' } : state));
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Board starring' }));
  await screen.findByRole('status');
  expect(screen.queryByText('You have not starred this Board.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Star Board' })).not.toBeInTheDocument();
  expect(screen.queryByText('secret')).not.toBeInTheDocument();
});
it('aborts a pending change on access withdrawal and ignores a late acknowledgment', async () => {
  let finish!: (response: Response) => void;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'PUT' ? new Promise<Response>(resolve => { finish = resolve; }) : response(state));
  vi.stubGlobal('fetch', fetch); const view = render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' })); await waitFor(() => expect(finish).toBeDefined());
  view.rerender(<BoardStarControl {...props} admitted={false} />);
  await screen.findByText('Board starring is unavailable.');
  expect(fetch.mock.calls.find(([, options]) => options?.method === 'PUT')![1]?.signal?.aborted).toBe(true);
  await act(async () => finish(new Response(null, { status: 204 })));
  expect(screen.queryByRole('button', { name: 'Retry same star change' })).not.toBeInTheDocument();
  expect(screen.queryByText('You have starred this Board.')).not.toBeInTheDocument();
});
it('re-admits current preference on reconnect and removes listeners when closed', async () => {
  let starred = false;
  const fetch = vi.fn(async (path: string) => path === '/me' ? response(profile) : response(starred ? { ...retained, starred } : state));
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open(); starred = true;
  fireEvent(window, new Event('online')); await screen.findByText('You have starred this Board.');
  fireEvent.click(screen.getByRole('button', { name: 'Done' }));
  const count = fetch.mock.calls.length; fireEvent(window, new Event('online')); await act(async () => {});
  expect(fetch).toHaveBeenCalledTimes(count);
});
it('never sends an unresolved receipt under a replacement account', async () => {
  let account = user;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response({ ...profile, id: account });
    if (options?.method === 'PUT') throw new Error('Lost response');
    return response(state);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  const retry = await screen.findByRole('button', { name: 'Retry same star change' });
  await waitFor(() => expect(retry).toBeEnabled()); account = other; fireEvent.click(retry);
  await screen.findByText('Your account changed. Close and reopen Board starring.');
  expect(fetch.mock.calls.filter(([, options]) => options?.method === 'PUT')).toHaveLength(1);
  expect(screen.queryByRole('button', { name: 'Retry same star change' })).not.toBeInTheDocument();
});
it('retires the receipt after a known access denial', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'PUT') {
      if (++writes === 1) throw new Error('Lost response');
      return response({ code: 'board_not_found' }, 404);
    }
    return response(state);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  const retry = await screen.findByRole('button', { name: 'Retry same star change' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText('Board starring is unavailable. Check access or sign in.');
  expect(screen.queryByRole('button', { name: 'Retry same star change' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Done' })).toBeEnabled();
});
it('drops an unresolved receipt when navigating to another Board', async () => {
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'PUT' ? Promise.reject(new Error('Lost response')) : response(state));
  vi.stubGlobal('fetch', fetch); const view = render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  await screen.findByRole('button', { name: 'Retry same star change' });
  view.rerender(<BoardStarControl {...props} boardId={other} />);
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry same star change' })).not.toBeInTheDocument();
});
it('reports fixed retry/reconnect outcomes without preferences, identifiers or diagnostics', async () => {
  configureActivityTelemetry(true); const reports: string[] = []; let writes = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') { reports.push(String(options?.body)); return new Response(null, { status: 204 }); }
    if (path === '/me') return response(profile);
    if (options?.method === 'PUT') { if (++writes === 1) throw new Error('private star diagnostic'); return new Response(null, { status: 204 }); }
    return response(state);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  const retry = await screen.findByRole('button', { name: 'Retry same star change' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByRole('button', { name: 'Star Board' });
  fireEvent(window, new Event('online'));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Star Board' })).toBeEnabled());
  await flushActivityTelemetry();
  const events = reports.flatMap(report => JSON.parse(report).events);
  for (const [action, kind] of [['board_star_disclosure', 'open'], ['board_star_read', 'reconnect'],
    ['board_star_change', 'use'], ['board_star_change', 'retry'], ['board_star_change', 'exception'],
    ['board_star_change', 'failure'], ['board_star_change', 'success']])
    expect(events).toContainEqual(expect.objectContaining({ action, kind }));
  for (const event of events) expect(Object.keys(event).every(key => ['action', 'kind', 'count', 'durationMs'].includes(key))).toBe(true);
  for (const value of [org, board, user, 'starred', 'diagnostic', 'Idempotency-Key', '/boards/'])
    expect(reports.join('')).not.toContain(value);
});
it.each([409, 403])('reports a known failure without retry intent or response text (%s)', async status => {
  configureActivityTelemetry(true); const reports: string[] = [];
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') { reports.push(String(options?.body)); return new Response(null, { status: 204 }); }
    if (path === '/me') return response(profile);
    if (options?.method === 'PUT') return response({ title: 'private server details' }, status);
    return response(state);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Star Board' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Done' })).toBeEnabled());
  await flushActivityTelemetry();
  const events = reports.flatMap(report => JSON.parse(report).events);
  expect(events).toContainEqual(expect.objectContaining({ action: 'board_star_change', kind: 'failure' }));
  if (status === 409) expect(events).toContainEqual(expect.objectContaining({ action: 'board_star_change', kind: 'conflict' }));
  expect(screen.queryByRole('button', { name: 'Retry same star change' })).not.toBeInTheDocument();
  expect(reports.join('')).not.toContain('private server details');
});
it.each([
  { ...state, starred: true },
  { ...state, version: -1 },
  { ...retained, version: 1.5 },
  { ...retained, updatedAt: null },
  { ...retained, updatedAt: 'private diagnostic' },
  { ...retained, createdAt: '2026-10-04T13:00:00Z' },
])('withholds invalid preference revision/clock combinations %#', async invalid => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : invalid)));
  render(<BoardStarControl {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Board starring' }));
  await screen.findByText('Unable to check your current star. Try again.');
  expect(screen.queryByRole('button', { name: 'Star Board' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Unstar Board' })).not.toBeInTheDocument();
  expect(screen.queryByText('private diagnostic')).not.toBeInTheDocument();
});
it('admits a retained historical preference with an explicitly unknown creation clock', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : { ...retained, createdAt: null })));
  render(<BoardStarControl {...props} />); await open();
  expect(screen.getByRole('button', { name: 'Star Board' })).toBeEnabled();
});
