import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, afterEach, expect, it, vi } from 'vitest';
import { BoardFilterControl } from './BoardFilterControl';
import type { BoardSnapshot } from '../../api/workManagement';
import { createBoardFilterChange, retainBoardFilterChange } from '../search/boardFilterChange';
const actor = '11111111-1111-4111-8111-111111111111', org = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', list = '44444444-4444-4444-8444-444444444444';
const card = { id: '55555555-5555-4555-8555-555555555555', organizationId: org, boardId: board, listId: list, title: 'Admitted match',
  description: null, rank: '500000000000000000000000000000', lifecycleState: 'active', version: 1 };
const label = { id: '66666666-6666-4666-8666-666666666666', organizationId: org, boardId: board, name: 'Priority', color: 'red', deleted: false };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' },
  lists: [{ list: { id: list, name: 'Planning', rank: 'a', lifecycleState: 'active' }, cards: [card] }],
  access: { canView: true, canEdit: false, canMove: false, canAdminister: false } };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function setup() {
  const state = { actor, loseFirstResponse: false, writes: 0, originals: new Map<string, unknown>(),
    switchedAfterPost: false, switchedAfterResult: false, switchedAfterLabels: false, switchedAfterMembers: false, foreignSource: false };
  const fetch = vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me') return response({ id: state.actor });
    if (path.includes('/filter-change?')) {
      state.writes++; const key = new Headers(init?.headers).get('Idempotency-Key')!;
      if (!state.originals.has(key)) state.originals.set(key, { eventId: key, eventType: 'BOARD_FILTER_CHANGED', actorId: actor,
        organizationId: org, boardId: state.foreignSource ? list : board, entityType: 'BoardFilter', entityId: key, version: 1,
        metadata: {}, createdAt: '2026-10-05T12:00:00.123456Z' });
      if (state.switchedAfterPost) state.actor = list;
      if (state.loseFirstResponse && state.writes === 1) throw new TypeError('Lost reply');
      return response(state.originals.get(key));
    }
    if (path.includes('/labels')) { if (state.switchedAfterLabels) state.actor = list; return response({ organizationId: org, boardId: board, items: [label], nextCursor: null }); }
    if (path.includes('/assignable-members')) { if (state.switchedAfterMembers) state.actor = list; return response({ organizationId: org, boardId: board, items: [{ userId: list, displayName: 'Taylor' }], nextCursor: null }); }
    if (path.includes('/cards?')) { if (state.switchedAfterResult) state.actor = list; return response({ organizationId: org, boardId: board, items: [card], nextCursor: null }); }
    throw new Error('Unexpected request path');
  }); vi.stubGlobal('fetch', fetch); return { state, fetch };
}
function mount(onRefresh = vi.fn()) { return { ...render(<MemoryRouter><BoardFilterControl snapshot={snapshot} disabled={false} onRefresh={onRefresh} /></MemoryRouter>), onRefresh }; }
async function open() { fireEvent.click(screen.getByRole('button', { name: 'Filter Board Cards' })); await screen.findByRole('checkbox', { name: 'Priority (red)' }); }
beforeEach(() => sessionStorage.clear()); afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });
it('admits Apply/Clear originals through the actual same-origin transport without emitting for read refresh', async () => {
  const { state, fetch } = setup(); const view = mount(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: ' roof ' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByRole('link', { name: 'Admitted match — Planning' }); await screen.findByText('Filter change acknowledged.');
  const post = fetch.mock.calls.find(([path]) => path.includes('/filter-change?'))!;
  expect(new URL(post[0], 'https://example.test').searchParams.get('keyword')).toBe('roof');
  expect(post[1]?.body).toBeUndefined(); const headers = new Headers(post[1]?.headers);
  expect(headers.get('X-StrataAI-Request')).toBe('1'); expect(headers.get('X-StrataAI-Expected-Actor')).toBe(actor);
  expect(state.writes).toBe(1);
  view.rerender(<MemoryRouter><BoardFilterControl snapshot={{ ...snapshot, lists: [...snapshot.lists] }} disabled={false} onRefresh={view.onRefresh} /></MemoryRouter>);
  await screen.findByRole('link', { name: 'Admitted match — Planning' }); expect(state.writes).toBe(1);
  fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
  await waitFor(() => expect(state.writes).toBe(2));
  await waitFor(() => expect(screen.queryByRole('link', { name: 'Admitted match — Planning' })).not.toBeInTheDocument());
  expect(state.originals.size).toBe(2); expect(Object.keys(sessionStorage).some(key => key.startsWith('strataai:board-filter-change:'))).toBe(false);
});
it('recovers the retained original after a lost response, close and remount without changing its intent', async () => {
  const { state, fetch } = setup(); state.loseFirstResponse = true; const first = mount(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: 'roof' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByText('The filter change is unconfirmed. Retry the original change to recover its acknowledgment.');
  expect(screen.getByRole('button', { name: 'Apply filters' })).toBeDisabled(); first.unmount(); mount(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: 'New draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Retry original filter change' }));
  await screen.findByRole('link', { name: 'Admitted match — Planning' });
  const posts = fetch.mock.calls.filter(([path]) => path.includes('/filter-change?')); expect(posts).toHaveLength(2);
  expect(posts[1][0]).toBe(posts[0][0]); expect(new Headers(posts[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(posts[0][1]?.headers).get('Idempotency-Key'));
  expect(state.originals.size).toBe(1);
  expect(JSON.parse(sessionStorage.getItem(`strataai:board-filter:v1:${actor}:${org}:${board}`)!).keyword).toBe('roof');
});
it('keeps the in-memory original through close/reopen when optional session storage is unavailable', async () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Storage unavailable'); });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage unavailable'); });
  const { state, fetch } = setup(); state.loseFirstResponse = true; mount(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: 'Original roof' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' })); await screen.findByText('The filter change is unconfirmed. Retry the original change to recover its acknowledgment.');
  fireEvent.click(screen.getByRole('button', { name: 'Close filters' })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Retry original filter change' })); await screen.findByRole('link', { name: 'Admitted match — Planning' });
  const posts = fetch.mock.calls.filter(([path]) => path.includes('/filter-change?')); expect(posts).toHaveLength(2);
  expect(posts[1][0]).toBe(posts[0][0]); expect(new Headers(posts[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(posts[0][1]?.headers).get('Idempotency-Key'));
  expect(state.originals.size).toBe(1);
});
it.each(['before', 'after'])('retires changed-account intent %s POST without exposing results or acknowledgment', async when => {
  const { state } = setup(); const view = mount(); await open();
  if (when === 'before') state.actor = list; else state.switchedAfterPost = true;
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(view.onRefresh).toHaveBeenCalled()); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(state.writes).toBe(when === 'before' ? 0 : 1); expect(screen.queryByText('Filter change acknowledged.')).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Admitted match — Planning' })).not.toBeInTheDocument();
});
it('withholds successful result pages after an account changes during the read', async () => {
  const { state } = setup(); const view = mount(); await open(); state.switchedAfterResult = true;
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(view.onRefresh).toHaveBeenCalled());
  expect(screen.queryByRole('link', { name: 'Admitted match — Planning' })).not.toBeInTheDocument();
});
it('withdraws idle personal criteria, directories and results after foreground account change without another POST', async () => {
  const { state } = setup(); const view = mount(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: 'Personal roof' } });
  fireEvent.click(screen.getByRole('button', { name: 'Choose assignees' })); await screen.findByRole('checkbox', { name: 'Taylor' });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' })); await screen.findByRole('link', { name: 'Admitted match — Planning' });
  state.actor = list; fireEvent(window, new Event('focus'));
  await waitFor(() => expect(view.onRefresh).toHaveBeenCalled());
  expect(screen.queryByDisplayValue('Personal roof')).not.toBeInTheDocument();
  expect(screen.queryByRole('checkbox', { name: 'Priority (red)' })).not.toBeInTheDocument(); expect(screen.queryByRole('checkbox', { name: 'Taylor' })).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Admitted match — Planning' })).not.toBeInTheDocument(); expect(state.writes).toBe(1);
});
it('recovers current pages through read-only reconnect without another filter change', async () => {
  const { state, fetch } = setup(); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByRole('link', { name: 'Admitted match — Planning' });
  const before = fetch.mock.calls.filter(([path]) => path.includes('/cards?')).length;
  fireEvent(window, new Event('online'));
  await waitFor(() => expect(fetch.mock.calls.filter(([path]) => path.includes('/cards?')).length).toBeGreaterThan(before));
  await screen.findByRole('link', { name: 'Admitted match — Planning' }); expect(state.writes).toBe(1);
});
it('withholds label choices when the opening account changes during directory IO', async () => {
  const { state } = setup(); state.switchedAfterLabels = true; const view = mount();
  fireEvent.click(screen.getByRole('button', { name: 'Filter Board Cards' })); await waitFor(() => expect(view.onRefresh).toHaveBeenCalled());
  expect(screen.queryByRole('checkbox', { name: 'Priority (red)' })).not.toBeInTheDocument(); expect(state.writes).toBe(0);
});
it('withholds assignees when the account changes during member directory IO', async () => {
  const { state } = setup(); const view = mount(); await open(); state.switchedAfterMembers = true;
  fireEvent.click(screen.getByRole('button', { name: 'Choose assignees' })); await waitFor(() => expect(view.onRefresh).toHaveBeenCalled());
  expect(screen.queryByRole('checkbox', { name: 'Taylor' })).not.toBeInTheDocument(); expect(state.writes).toBe(0);
});
it('restores owned keyboard focus to original retry and Apply after recovery', async () => {
  const { state } = setup(); state.loseFirstResponse = true; mount(); await open();
  const apply = screen.getByRole('button', { name: 'Apply filters' }); apply.focus(); fireEvent.click(apply);
  const retry = await screen.findByRole('button', { name: 'Retry original filter change' }); await waitFor(() => expect(retry).toHaveFocus());
  fireEvent.click(retry); await screen.findByRole('link', { name: 'Admitted match — Planning' }); await waitFor(() => expect(apply).toHaveFocus());
});
it('refuses a replacement request when another original appears after opening', async () => {
  const { state } = setup(); mount(); await open();
  const original = createBoardFilterChange({ actor, organization: org, board }, 'apply', { keyword: 'Original', labels: [], members: [], match: 'all' });
  retainBoardFilterChange(sessionStorage, original);
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByText('An earlier filter change is unconfirmed. Retry the original change.'); expect(state.writes).toBe(0);
  fireEvent.click(screen.getByRole('button', { name: 'Retry original filter change' }));
  await screen.findByRole('link', { name: 'Admitted match — Planning' }); expect(state.originals.has(original.key)).toBe(true); expect(state.writes).toBe(1);
});
it('keeps an unconfirmed original and withholds criteria/result commit for a foreign-scope source', async () => {
  const { state } = setup(); state.foreignSource = true; mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByRole('button', { name: 'Retry original filter change' });
  expect(screen.queryByText('Filter change acknowledged.')).not.toBeInTheDocument(); expect(screen.queryByRole('link', { name: 'Admitted match — Planning' })).not.toBeInTheDocument();
});
it('cancels a pending account proof on close and recovers without dispatching the closed view', async () => {
  const { fetch, state } = setup(); mount(); await open();
  let release!: (value: Response) => void;
  fetch.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' })); await waitFor(() => expect(release).toBeTypeOf('function'));
  fireEvent.click(screen.getByRole('button', { name: 'Close filters' })); await act(async () => release(response({ id: actor })));
  expect(state.writes).toBe(0); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Retry original filter change' }));
  await screen.findByRole('link', { name: 'Admitted match — Planning' }); expect(state.writes).toBe(1);
});
