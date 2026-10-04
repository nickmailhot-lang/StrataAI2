import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BoardStarHistory } from './BoardStarHistoryControl';
import { parseStarHistory } from './boardStarHistory';
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
