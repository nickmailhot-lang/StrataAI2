import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BoardStarHistory } from './BoardStarHistoryControl';
import { parseStarHistory } from './boardStarHistory';
import { watchIdentity } from '../auth/identityLive';
import { Button, Dialog } from '@mui/material';
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222';
const user = '33333333-3333-3333-3333-333333333333';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const item = { organizationId: org, boardId: board, actorId: user, entityId: '44444444-4444-4444-4444-444444444444',
  eventId: '55555555-5555-5555-5555-555555555555', eventType: 'BOARD_STARRED', entityType: 'UserBoardPreference',
  createdAt: '2026-10-04T12:00:00Z', version: 1, metadata: {} };
const page = { organizationId: org, boardId: board, userId: user, items: [item], nextAfter: null };
const props = { organizationId: org, boardId: board, userId: user, version: 1, unavailable: false, onDenied: vi.fn() };
const response = (value: unknown) => new Response(JSON.stringify(value));
afterEach(() => { vi.unstubAllGlobals(); });
beforeEach(() => { vi.mocked(watchIdentity).mockClear(); });

it('recovers account preferences in an open continuation page without changing history identity or cursor', async () => {
  let timezone = 'Pacific/Honolulu';
  const first = { ...page, items: Array.from({ length: 50 }, (_, index) => ({ ...item, version: index + 1,
    eventId: '55555555-5555-5555-5555-' + String(index + 1).padStart(12, '0') })), nextAfter: 50 };
  const last = { ...page, items: [{ ...item, version: 51, createdAt: '2026-10-04T07:00:00.123456Z' }] };
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? { ...profile, locale: 'en-US', timezone }
    : path.endsWith('after=50') ? last : first));
  vi.stubGlobal('fetch', fetch); render(<BoardStarHistory {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(50));
  fireEvent.click(screen.getByRole('button', { name: 'Next star history page' }));
  await screen.findByText('You changed your Board star (revision 51).');
  expect(document.querySelector('time')).toHaveTextContent('Oct 3, 2026, 21:00 HST');
  expect(watchIdentity).toHaveBeenCalledTimes(1);
  timezone = 'Asia/Tokyo'; act(() => { vi.mocked(watchIdentity).mock.calls[0][0].invalidate(); });
  await screen.findByText('Oct 4, 2026, 16:00 GMT+9');
  expect(document.querySelector('time')).toHaveAttribute('datetime', last.items[0].createdAt);
  expect(fetch.mock.calls.filter(([path]) => path !== '/me').map(([path]) => path)).toEqual([
    `/boards/${board}/star/events?after=0`, `/boards/${board}/star/events?after=50`, `/boards/${board}/star/events?after=50`,
  ]);
});

it('recovers periodically without stream delivery and stops timers and subscription on close', async () => {
  vi.useFakeTimers();
  try {
    let timezone = 'Pacific/Honolulu';
    const fetch = vi.fn(async (path: string) => response(path === '/me' ? { ...profile, locale: 'en-US', timezone }
      : { ...page, items: [{ ...item, createdAt: '2026-10-04T07:00:00.123456Z' }] }));
    vi.stubGlobal('fetch', fetch); render(<BoardStarHistory {...props} />);
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Review your star history' })); });
    timezone = 'Asia/Tokyo'; await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
    expect(document.querySelector('time')).toHaveTextContent('Oct 4, 2026, 16:00 GMT+9');
    expect(fetch.mock.calls.filter(([path]) => path !== '/me')).toHaveLength(2);
    const dispose = vi.mocked(watchIdentity).mock.results[0].value;
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Close star history' })); });
    expect(dispose).toHaveBeenCalledTimes(1);
    await act(async () => { window.dispatchEvent(new Event('online')); await vi.advanceTimersByTimeAsync(20_000); });
    expect(fetch.mock.calls.filter(([path]) => path !== '/me')).toHaveLength(2);
  } finally { vi.useRealTimers(); }
});

it('queues one recovery without aborting a pending protected page', async () => {
  let reads = 0; let finish!: (value: Response) => void; let signal: AbortSignal | undefined;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (++reads === 2) { signal = options?.signal ?? undefined; return new Promise<Response>(resolve => { finish = resolve; }); }
    return response(page);
  });
  vi.stubGlobal('fetch', fetch); render(<BoardStarHistory {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' })); await screen.findByRole('listitem');
  const delivered = vi.mocked(watchIdentity).mock.calls[0][0].invalidate;
  act(delivered); await waitFor(() => expect(finish).toBeDefined());
  act(() => { delivered(); delivered(); window.dispatchEvent(new Event('online')); });
  expect(reads).toBe(2); expect(signal?.aborted).toBe(false);
  await act(async () => { finish(response(page)); }); await waitFor(() => expect(reads).toBe(3));
  await screen.findByRole('listitem'); expect(watchIdentity).toHaveBeenCalledTimes(1);
});

it('retains the focused action through background recovery inside its dialog', async () => {
  let reads = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return response(profile); reads++; return response(page);
  }));
  render(<Dialog open><BoardStarHistory {...props} /></Dialog>);
  const review = screen.getByRole('button', { name: 'Review your star history' }); review.focus(); fireEvent.click(review);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Close star history' })).toHaveFocus());
  review.focus(); act(() => { window.dispatchEvent(new Event('online')); });
  await waitFor(() => expect(reads).toBe(2)); await waitFor(() => expect(review).toHaveFocus());
});
it('returns focus to the history opener when closing removes its activated button inside a dialog', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : page)));
  render(<Dialog open transitionDuration={0}><Button>Check current star</Button><BoardStarHistory {...props} /></Dialog>);
  const review = screen.getByRole('button', { name: 'Review your star history' }); act(() => review.focus()); fireEvent.click(review);
  const close = await screen.findByRole('button', { name: 'Close star history' });
  await waitFor(() => expect(close).toHaveFocus()); fireEvent.click(close);
  expect(screen.queryByRole('region', { name: 'Your star history' })).not.toBeInTheDocument();
  await waitFor(() => expect(review).toHaveFocus());
});

it('keeps another dialog control focused when a delayed history read completes', async () => {
  let finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? response(profile)
    : new Promise<Response>(resolve => { finish = resolve; })));
  render(<Dialog open><BoardStarHistory {...props} /><Button>Another control</Button></Dialog>);
  const review = screen.getByRole('button', { name: 'Review your star history' }); review.focus(); fireEvent.click(review);
  await waitFor(() => expect(finish).toBeDefined()); const other = screen.getByRole('button', { name: 'Another control' }); other.focus();
  await act(async () => { finish(response(page)); }); await screen.findByRole('listitem'); expect(other).toHaveFocus();
});

it.each([401, 403, 404])('retires recovery after %s denial and resumes only with fresh parent admission', async status => {
  let fail = false; let reads = 0; const denied = vi.fn();
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return response(profile);
    reads++; return fail ? new Response('{}', { status }) : response(page);
  }));
  const view = render(<BoardStarHistory {...props} onDenied={denied} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' })); await screen.findByRole('listitem');
  fail = true; act(() => { window.dispatchEvent(new Event('online')); });
  await screen.findByText('Your star history is unavailable. Refresh the Board to check access.');
  expect(denied).toHaveBeenCalledTimes(1); expect(screen.queryByRole('listitem')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Retry star history' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Review your star history' })).toBeDisabled();
  expect(vi.mocked(watchIdentity).mock.results[0].value).toHaveBeenCalledTimes(1);
  act(() => { window.dispatchEvent(new Event('online')); window.dispatchEvent(new Event('focus')); }); expect(reads).toBe(2);
  fail = false; view.rerender(<BoardStarHistory {...props} onDenied={denied} unavailable />);
  view.rerender(<BoardStarHistory {...props} onDenied={denied} />);
  await screen.findByRole('listitem'); expect(reads).toBe(3); expect(denied).toHaveBeenCalledTimes(1);
});

// AUTH-FR-010 / PRD-02-TC-08: final account admission owns date display.
it('formats immutable personal history time using final confirmed preferences and an explicit zone label', async () => {
  let reads = 0; let finalZone = 'Pacific/Honolulu';
  const source = { ...item, createdAt: '2026-10-04T07:00:00.123456Z' };
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me'
    ? { ...profile, locale: 'en-US', timezone: ++reads === 1 ? 'Asia/Tokyo' : finalZone }
    : { ...page, items: [source] })));
  render(<BoardStarHistory {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await screen.findByText('You changed your Board star (revision 1).');
  const timestamp = document.querySelector('time')!;
  expect(timestamp).toHaveAttribute('datetime', source.createdAt);
  expect(timestamp).toHaveTextContent('Oct 3, 2026, 21:00 HST');
  expect(reads).toBe(2);
  finalZone = 'Asia/Tokyo'; fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await screen.findByText('Oct 4, 2026, 16:00 GMT+9');
  expect(document.querySelector('time')).toHaveAttribute('datetime', source.createdAt);
  expect(reads).toBe(4);
});
it('validates increasing private pages, continuation and stable preference identity', () => {
  expect(parseStarHistory(page, org, board, user).items).toHaveLength(1);
  const items = Array.from({ length: 50 }, (_, index) => ({ ...item, version: index + 1,
    eventId: '55555555-5555-5555-5555-' + String(index + 1).padStart(12,'0') }));
  expect(parseStarHistory({ ...page, items, nextAfter: 50 }, org, board, user).nextAfter).toBe(50);
  expect(() => parseStarHistory({ ...page, items, nextAfter: 49 }, org, board, user)).toThrow();
  expect(() => parseStarHistory(page, org, board, user, 1)).toThrow();
  expect(() => parseStarHistory(page, org, board, user, 0, user)).toThrow();
  expect(() => parseStarHistory({ ...page, userId: org }, org, board, user)).toThrow();
});
it('loads only on disclosure, presents fixed personal labels and restores keyboard focus on close', async () => {
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? profile : page));
  vi.stubGlobal('fetch', fetch); render(<BoardStarHistory {...props} />);
  expect(fetch).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await screen.findByText('You changed your Board star (revision 1).');
  expect(fetch.mock.calls.map(([path]) => path)).toEqual(['/me','/boards/' + board + '/star/events?after=0','/me']);
  expect(screen.getByRole('button', { name: 'Next star history page' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Close star history' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review your star history' })).toHaveFocus());
  expect(screen.queryByText('You changed your Board star (revision 1).')).not.toBeInTheDocument();
});
it('withholds history when the post-read account changes and publishes denial', async () => {
  let reads = 0; const denied = vi.fn();
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? { ...profile, id: ++reads === 1 ? user : org } : page)));
  render(<BoardStarHistory {...props} onDenied={denied} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await waitFor(() => expect(denied).toHaveBeenCalledTimes(1));
  expect(screen.queryByText('You changed your Board star (revision 1).')).not.toBeInTheDocument();
});
it('returns focus when closed during a pending read and fences the late private page', async () => {
  let finish!: (value: Response) => void; let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    signal = options?.signal ?? undefined; return new Promise<Response>(resolve => { finish = resolve; });
  }));
  render(<BoardStarHistory {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await waitFor(() => expect(finish).toBeDefined());
  fireEvent.click(screen.getByRole('button', { name: 'Close star history' }));
  expect(signal?.aborted).toBe(true);
  expect(screen.getByRole('button', { name: 'Review your star history' })).toHaveFocus();
  finish(response(page));
  await waitFor(() => expect(screen.queryByRole('region', { name: 'Your star history' })).not.toBeInTheDocument());
  expect(screen.queryByText('You changed your Board star (revision 1).')).not.toBeInTheDocument();
});
it('aborts on admission withdrawal and ignores a late protected page', async () => {
  let finish!: (value: Response) => void; let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    signal = options?.signal ?? undefined; return new Promise<Response>(resolve => { finish = resolve; });
  }));
  const view = render(<BoardStarHistory {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review your star history' }));
  await waitFor(() => expect(finish).toBeDefined());
  view.rerender(<BoardStarHistory {...props} unavailable />);
  expect(signal?.aborted).toBe(true); finish(response(page));
  await waitFor(() => expect(screen.queryByText('You changed your Board star (revision 1).')).not.toBeInTheDocument());
});
