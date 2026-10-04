import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { CardCopyControl } from './CardCopyControl';
import type { BoardSnapshot } from '../../api/workManagement';

const id = (n: number) => `00000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;
const card = { id: id(1), title: '<img> Source', description: null, rank: '500000000000000000000000000000', version: 4 };
const snapshot: BoardSnapshot = { board: { id: id(2), organizationId: id(3), name: 'Source', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: true }, lists: [{ list: { id: id(4), name: 'Source List', rank: '1', lifecycleState: 'active' }, cards: [card] }] };
const target: BoardSnapshot = { ...snapshot, board: { ...snapshot.board, id: id(5), name: 'Destination' }, lists: [
  { list: { id: id(6), name: 'Destination List', rank: '1', lifecycleState: 'active' }, cards: [] },
  { list: { id: id(7), name: 'Archived', rank: '2', lifecycleState: 'archived' }, cards: [] }] };
const ack = { ...card, id: id(10), organizationId: id(3), boardId: id(5), listId: id(6), title: 'Fresh copy', version: 1, lifecycleState: 'active' };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const props = { card, selectedCardId: card.id, snapshot, disabled: false, unavailable: false,
  onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() };
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });
function mock(copy: (options: RequestInit) => Promise<Response> = () => Promise.resolve(reply(ack))) {
  const mutations: RequestInit[] = [];
  const fetcher = vi.fn((path: string, options: RequestInit) => {
    if (path === '/me') return Promise.resolve(reply({ id: id(8) }));
    if (path === `/organizations/${id(3)}/boards`) return Promise.resolve(reply([{ id: id(2), name: 'Source', version: 1 }, { id: id(5), name: 'Destination', version: 1 }]));
    if (path === `/boards/${id(5)}`) return Promise.resolve(reply(target));
    if (path === `/boards/${id(2)}`) return Promise.resolve(reply(snapshot));
    if (path === `/cards/${card.id}/copy`) { mutations.push(options); return copy(options); }
    throw new Error('Unexpected request');
  });
  vi.stubGlobal('fetch', fetcher); return { fetcher, mutations };
}
const control = (extra = {}) => <MemoryRouter><CardCopyControl {...props} {...extra} /></MemoryRouter>;
async function choose(board = 'Destination', list = 'Destination List') {
  fireEvent.click(screen.getByRole('button', { name: 'Copy Card' }));
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Copy destination Board' })).not.toHaveAttribute('aria-disabled', 'true'));
  fireEvent.change(screen.getByRole('textbox', { name: 'Copied Card title' }), { target: { value: ' Fresh copy ' } });
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Copy destination Board' }));
  fireEvent.click(await screen.findByRole('option', { name: board }));
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Copy destination List' })).not.toHaveAttribute('aria-disabled', 'true'));
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Copy destination List' }));
  expect(screen.queryByRole('option', { name: 'Archived' })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('option', { name: list }));
}
it('reviews copy defaults and binds title, source, version and new destination identity', async () => {
  const { mutations } = mock(); const view = render(control()); await choose();
  expect(screen.getByText(/attachments and covers are not copied/)).toBeVisible(); expect(view.container.querySelector('img')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Card copy' }));
  const link = await screen.findByRole('link', { name: 'Open copied Card' });
  expect(link).toHaveAttribute('href', `/app/${id(3)}/boards/${id(5)}/cards/${id(10)}`);
  expect(link).toHaveFocus();
  expect(JSON.parse(mutations[0].body as string)).toEqual({ sourceBoardId: id(2), destinationListId: id(6), title: 'Fresh copy', expectedVersion: 4 });
});
it('supports the source Board as a copy destination', async () => {
  const { mutations } = mock(() => Promise.resolve(reply({ ...ack, boardId: id(2), listId: id(4) })));
  render(control()); await choose('Source', 'Source List'); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card copy' }));
  await screen.findByRole('link', { name: 'Open copied Card' }); expect(JSON.parse(mutations[0].body as string).destinationListId).toBe(id(4));
});
it('retains the exact title/body/key after uncertainty and source removal', async () => {
  let attempts = 0; const { mutations } = mock(() => ++attempts === 1 ? Promise.reject(new Error('Lost reply')) : Promise.resolve(reply(ack)));
  const view = render(control()); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card copy' }));
  await screen.findByRole('button', { name: 'Retry this Card copy' });
  view.rerender(control({ card: undefined, snapshot: { ...snapshot, lists: [] } }));
  expect(screen.getByRole('textbox', { name: 'Copied Card title' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Cancel Card copy' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this Card copy' })); await screen.findByRole('link', { name: 'Open copied Card' });
  expect(mutations).toHaveLength(2); expect(mutations[1].body).toBe(mutations[0].body);
  expect(new Headers(mutations[1].headers).get('Idempotency-Key')).toBe(new Headers(mutations[0].headers).get('Idempotency-Key'));
});
it('refuses stale source review and empty copy title', async () => {
  const { mutations } = mock(); const view = render(control()); await choose();
  fireEvent.change(screen.getByRole('textbox', { name: 'Copied Card title' }), { target: { value: ' ' } });
  expect(screen.getByRole('button', { name: 'Confirm Card copy' })).toBeDisabled();
  view.rerender(control({ card: { ...card, version: 5 } })); expect(screen.getByRole('button', { name: 'Confirm Card copy' })).toBeDisabled();
  expect(mutations).toHaveLength(0);
});
it('returns keyboard focus to the source action after canceling the review', async () => {
  mock(); render(control()); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Cancel Card copy' }));
  expect(screen.getByRole('button', { name: 'Copy Card' })).toHaveFocus();
});
it.each([401, 403, 404])('clears choices and copy recovery on terminal refusal %i', async status => {
  mock(() => Promise.resolve(reply({}, status))); render(control()); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card copy' }));
  await screen.findByText('This copy is unavailable.'); expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry this Card copy' })).not.toBeInTheDocument();
});
it('does not transmit an original retry under a different account', async () => {
  const { fetcher, mutations } = mock(() => Promise.reject(new Error('Lost reply'))); render(control()); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Card copy' })); await screen.findByRole('button', { name: 'Retry this Card copy' });
  fetcher.mockImplementation((path: string) => { if (path === '/me') return Promise.resolve(reply({ id: id(9) })); throw new Error('Must not transmit'); });
  fireEvent.click(screen.getByRole('button', { name: 'Retry this Card copy' })); await screen.findByText('This copy is unavailable.'); expect(mutations).toHaveLength(1);
});
it.each([{ id: card.id }, { organizationId: id(99) }, { listId: id(7) }, { version: 4 }, { title: 'Other' }])('keeps original recovery when acknowledgment scope is malformed %j', async changed => {
  mock(() => Promise.resolve(reply({ ...ack, ...changed }))); render(control()); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card copy' }));
  await screen.findByRole('button', { name: 'Retry this Card copy' }); expect(screen.queryByRole('link', { name: 'Open copied Card' })).not.toBeInTheDocument();
});
