import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ComponentProps } from 'react';
import { BoardFilterControl } from './BoardFilterControl';
import type { BoardSnapshot } from '../../api/workManagement';
import { filteredBoardCanvas } from './boardFilterCanvas';
const board = '11111111-1111-1111-1111-111111111111', org = '22222222-2222-2222-2222-222222222222';
const actor = '33333333-3333-3333-3333-333333333333', list = '44444444-4444-4444-4444-444444444444';
const label = { id: '55555555-5555-5555-5555-555555555555', organizationId: org, boardId: board, name: 'Priority', color: 'red', deleted: false };
const card = { id: '66666666-6666-6666-6666-666666666666', organizationId: org, boardId: board, listId: list, title: 'Persisted match', description: null, rank: '500000000000000000000000000000', lifecycleState: 'active', version: 3 };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' }, lists: [{ list: { id: list, name: 'Planning', rank: 'a', lifecycleState: 'active' }, cards: [] }], access: { canView: true, canEdit: false, canAdminister: false, canMove: false } };
const props = () => ({ snapshot, disabled: false, onRefresh: vi.fn() });
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const choices = (items = [label], nextCursor: string | null = null) => response({ organizationId: org, boardId: board, items, nextCursor });
const results = (items = [card], nextCursor: string | null = null) => response({ organizationId: org, boardId: board, items, nextCursor });
const storage = (id = actor) => `strataai:board-filter:v1:${id}:${org}:${board}`;
function mount(p: ComponentProps<typeof BoardFilterControl> = props()) { return render(<MemoryRouter><BoardFilterControl {...p} /></MemoryRouter>); }
async function open() { fireEvent.click(screen.getByRole('button', { name: 'Filter Board Cards' })); await screen.findByRole('checkbox', { name: 'Priority (red)' }); }
beforeEach(() => sessionStorage.clear()); afterEach(() => vi.unstubAllGlobals());
it('lets a viewer apply canonical label and keyword predicates and persists only criteria under their identity', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices()).mockResolvedValueOnce(results()); vi.stubGlobal('fetch', fetch); mount(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: ' roof ' } }); fireEvent.click(screen.getByRole('checkbox', { name: 'Priority (red)' }));
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  const link = await screen.findByRole('link', { name: 'Persisted match — Planning' }); expect(link).toHaveAttribute('href', `/app/${org}/boards/${board}/cards/${card.id}`);
  const query = new URL(fetch.mock.calls[2][0], 'https://example.test').searchParams;
  expect(query.get('keyword')).toBe('roof'); expect(query.get('labels')).toBe(label.id); expect(query.get('match')).toBe('all');
  expect(JSON.parse(sessionStorage.getItem(storage())!)).toEqual({ keyword: 'roof', labels: [label.id], match: 'all' });
  expect(sessionStorage.getItem(storage())).not.toContain('Persisted match');
  fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
  expect(sessionStorage.getItem(storage())).toBeNull(); expect(screen.queryByRole('link', { name: 'Persisted match — Planning' })).not.toBeInTheDocument();
});
it('restores valid criteria and does not reuse another signed-in user’s criteria', async () => {
  sessionStorage.setItem(storage(), JSON.stringify({ keyword: 'Saved text', labels: [label.id], match: 'any' }));
  const fetch = vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices())
    .mockResolvedValueOnce(response({ id: '77777777-7777-7777-7777-777777777777' })).mockResolvedValueOnce(choices());
  vi.stubGlobal('fetch', fetch); mount(); await open(); expect(screen.getByLabelText('Card keyword')).toHaveValue('Saved text'); expect(screen.getByRole('checkbox')).toBeChecked();
  fireEvent.click(screen.getByRole('button', { name: 'Close filters' })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await open(); expect(screen.getByLabelText('Card keyword')).toHaveValue(''); expect(screen.getByRole('checkbox')).not.toBeChecked();
});
it('keeps selected IDs while paging label choices', async () => {
  const extra = Array.from({ length: 49 }, (_, i) => ({ ...label, id: `88888888-8888-8888-8888-${String(i + 1).padStart(12, '0')}`, name: `Label ${i}` }));
  const next = { ...label, id: '99999999-9999-9999-9999-999999999999', name: 'Next choice' };
  const fetch = vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices([label, ...extra], extra.at(-1)!.id))
    .mockResolvedValueOnce(choices([next])).mockResolvedValueOnce(results());
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('checkbox', { name: 'Priority (red)' }));
  fireEvent.click(screen.getByRole('button', { name: 'Next label choices' })); fireEvent.click(await screen.findByRole('checkbox', { name: 'Next choice (red)' }));
  expect(screen.getByText('2 selected labels')).toBeInTheDocument(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByRole('link', { name: 'Persisted match — Planning' });
  expect(new URL(fetch.mock.calls[3][0], 'https://example.test').searchParams.get('labels')).toBe(`${label.id},${next.id}`);
});
it('paginates Cards without accumulating an unbounded result set', async () => {
  const items = Array.from({ length: 50 }, (_, i) => ({ ...card, id: `aaaaaaaa-aaaa-aaaa-aaaa-${String(i + 1).padStart(12, '0')}`, title: `Page ${i}` }));
  const fetch = vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices())
    .mockResolvedValueOnce(results(items, items.at(-1)!.id)).mockResolvedValueOnce(results());
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Next filtered Cards' })); await screen.findByRole('link', { name: 'Persisted match — Planning' });
  expect(screen.queryByRole('link', { name: 'Page 0 — Planning' })).not.toBeInTheDocument();
  expect(new URL(fetch.mock.calls[3][0], 'https://example.test').searchParams.get('after')).toBe(items.at(-1)!.id);
});
it('retires late result responses after a canonical refresh', async () => {
  let resolve!: (value: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices())
    .mockReturnValueOnce(new Promise<Response>(done => { resolve = done; })).mockResolvedValueOnce(choices([{ ...label, name: 'Updated Priority' }]))
    .mockResolvedValueOnce(results([{ ...card, title: 'Fresh match' }]));
  vi.stubGlobal('fetch', fetch); const p = props(); const view = mount(p); await open(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(resolve).toBeDefined());
  view.rerender(<MemoryRouter><BoardFilterControl {...p} snapshot={{ ...snapshot, lists: [...snapshot.lists] }} /></MemoryRouter>);
  await screen.findByRole('link', { name: 'Fresh match — Planning' }); await act(async () => resolve(results()));
  expect(screen.getByRole('checkbox', { name: 'Updated Priority (red)' })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Persisted match — Planning' })).not.toBeInTheDocument();
});
it.each([401, 403, 404])('clears results and refreshes Board admission after result denial %s', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices()).mockResolvedValueOnce(response({ detail: 'Private message' }, status)));
  const p = props(); mount(p); await open(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(screen.queryByText('Private message')).not.toBeInTheDocument(); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});
it('rejects a successful page from another Board', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices())
    .mockResolvedValueOnce(response({ organizationId: org, boardId: org, items: [card], nextCursor: null })));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByText('Filtered Cards could not be loaded. Try again or refresh the Board.'); expect(screen.queryByText('Persisted match — Planning')).not.toBeInTheDocument();
});
it('ignores invalid saved criteria and explains empty choices and results', async () => {
  sessionStorage.setItem(storage(), JSON.stringify({ keyword: 'Untrusted saved value', labels: ['invalid-id'], match: 'all' }));
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices([])).mockResolvedValueOnce(results([])));
  mount(); fireEvent.click(screen.getByRole('button', { name: 'Filter Board Cards' }));
  await screen.findByText('No labels on this choice page. Use a keyword, or reload choices.');
  expect(screen.getByLabelText('Card keyword')).toHaveValue(''); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByText('No Cards match these filters.');
});
it('projects a fresh bounded page onto canonical Lists, restores it for the admitted actor, and clears it', async () => {
  const other = { ...card, id: '77777777-7777-7777-7777-777777777777', title: 'Other Card' };
  const canonical = { ...snapshot, lists: [{ ...snapshot.lists[0], cards: [other, card] }, { list: { ...snapshot.lists[0].list, id: org, name: 'Empty List' }, cards: [] }] };
  const onCanvasChange = vi.fn(); const p = { ...props(), snapshot: canonical, onCanvasChange };
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices())
    .mockResolvedValueOnce(results()).mockResolvedValueOnce(results())
    .mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices()).mockResolvedValueOnce(results()));
  const view = mount(p); await open(); fireEvent.click(screen.getByRole('checkbox', { name: 'Priority (red)' }));
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Show this page on Board' }));
  await screen.findByText('Filtered Board: 1 matching Cards on this page.');
  const page = onCanvasChange.mock.calls.at(-1)![0];
  expect(filteredBoardCanvas(canonical, page).lists.map(l => l.cards.map(c => c.id))).toEqual([[card.id], []]);
  expect(canonical.lists[0].cards).toEqual([other, card]);
  expect(JSON.parse(sessionStorage.getItem(storage())!)).toEqual({ keyword: '', labels: [label.id], match: 'all', canvas: true });
  view.unmount(); const restored = mount(p);
  await screen.findByText('Filtered Board: 1 matching Cards on this page.');
  fireEvent.click(screen.getByRole('button', { name: 'Clear Board filters' }));
  await waitFor(() => expect(onCanvasChange).toHaveBeenLastCalledWith(undefined));
  expect(sessionStorage.getItem(storage())).toBeNull(); restored.unmount();
});
it('never restores another actor’s saved canvas predicates', async () => {
  sessionStorage.setItem(storage(), JSON.stringify({ keyword: 'Private criteria', labels: [label.id], match: 'all', canvas: true }));
  const fetch = vi.fn().mockResolvedValueOnce(response({ id: org })).mockResolvedValueOnce(choices()); vi.stubGlobal('fetch', fetch);
  const onCanvasChange = vi.fn(); mount({ ...props(), onCanvasChange });
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  expect(fetch.mock.calls.some(call => String(call[0]).includes('/cards?'))).toBe(false);
  expect(onCanvasChange).toHaveBeenLastCalledWith(undefined);
  expect(screen.queryByText(/Filtered Board:/)).not.toBeInTheDocument();
});
it('restores the new Board’s saved canvas after changing scope in the mounted control', async () => {
  const nextBoard = '99999999-9999-9999-9999-999999999999';
  const criteria = { keyword: '', labels: [], match: 'all', canvas: true };
  sessionStorage.setItem(storage(), JSON.stringify(criteria));
  sessionStorage.setItem(`strataai:board-filter:v1:${actor}:${org}:${nextBoard}`, JSON.stringify(criteria));
  const fetch = vi.fn().mockImplementation(async (url: string) => {
    if (url === '/me') return response({ id: actor });
    const currentBoard = url.includes(nextBoard) ? nextBoard : board;
    return response({ organizationId: org, boardId: currentBoard, items: [], nextCursor: null });
  }); vi.stubGlobal('fetch', fetch);
  const onCanvasChange = vi.fn(); const p = { ...props(), onCanvasChange }; const view = mount(p);
  await screen.findByText('Filtered Board: 0 matching Cards on this page.');
  const nextSnapshot = { ...snapshot, board: { ...snapshot.board, id: nextBoard } };
  view.rerender(<MemoryRouter><BoardFilterControl {...p} snapshot={nextSnapshot} /></MemoryRouter>);
  await waitFor(() => expect(onCanvasChange).toHaveBeenLastCalledWith({ snapshot: nextSnapshot, items: [] }));
  await waitFor(() => expect(fetch.mock.calls.some(call => String(call[0]).includes(`${nextBoard}/cards?`))).toBe(true));
});
it('refreshes canonical state rather than projecting a Card from a newer result revision', async () => {
  const canonical = { ...snapshot, lists: [{ ...snapshot.lists[0], cards: [card] }] };
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ id: actor })).mockResolvedValueOnce(choices()).mockResolvedValueOnce(results([{ ...card, version: 4 }])));
  const p = { ...props(), snapshot: canonical, onCanvasChange: vi.fn() }; mount(p); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(p.onRefresh).toHaveBeenCalledOnce());
  expect(screen.queryByRole('button', { name: 'Show this page on Board' })).not.toBeInTheDocument();
  expect(p.onCanvasChange).toHaveBeenLastCalledWith(undefined);
});
it('keeps all Lists and canonical order and hides results from a superseded snapshot', () => {
  const second = { ...card, id: actor, title: 'Second' };
  const canonical = { ...snapshot, lists: [{ ...snapshot.lists[0], cards: [card, second] }] };
  expect(filteredBoardCanvas(canonical, { snapshot: canonical, items: [second, card] }).lists[0].cards).toEqual([card, second]);
  expect(filteredBoardCanvas({ ...canonical }, { snapshot: canonical, items: [card] }).lists[0].cards).toEqual([]);
  expect(filteredBoardCanvas(canonical, { snapshot: canonical, items: [{ ...card, listId: actor }] }).lists[0].cards).toEqual([]);
});
