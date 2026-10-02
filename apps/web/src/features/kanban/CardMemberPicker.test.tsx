import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardMemberPicker } from './CardMemberPicker';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';
const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222', id = '33333333-3333-3333-3333-333333333333', userId = '44444444-4444-4444-4444-444444444444';
const card: WorkCard = { id, title: 'Card', description: null, rank: '500000000000000000000000000000', version: 1 };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' }, access: { canView: true, canEdit: true, canAdminister: true, canMove: true }, lists: [{ list: { id: 'list', name: 'List', rank: card.rank, lifecycleState: 'active' }, cards: [card] }] };
const props = () => ({ cardId: id, card, snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() });
const options = { cardId: id, organizationId: org, boardId: board, cardVersion: 1, nextCursor: null, items: [{ userId, displayName: 'Taylor', assigned: false }] };
const ack = { card: { ...card, organizationId: org, boardId: board, version: 2 }, userId, assigned: true, changed: true };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
async function open() { fireEvent.click(screen.getByRole('button', { name: 'Edit Card assignees' })); return screen.findByRole('button', { name: 'Assign Taylor' }); }
afterEach(() => vi.unstubAllGlobals());

it('assigns an eligible named member against the current revision and restores keyboard focus', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(options)).mockResolvedValueOnce(response(ack)); vi.stubGlobal('fetch', fetch);
  const p = props(); render(<CardMemberPicker {...p} />); fireEvent.click(await open()); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[0][0]).toBe(`/cards/${id}/member-options`);
  expect(fetch.mock.calls[1][0]).toBe(`/cards/${id}/members/${userId}?version=1`); expect(fetch.mock.calls[1][1].method).toBe('PUT');
  expect(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key')).toMatch(/^[a-f0-9-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Edit Card assignees' })).toHaveFocus());
});
it('unassigns with DELETE and accepts a no-op receipt at the original revision', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response({ ...options, items: [{ ...options.items[0], assigned: true }] }))
    .mockResolvedValueOnce(response({ ...ack, assigned: false, changed: false, card: { ...ack.card, version: 1 } })); vi.stubGlobal('fetch', fetch);
  const p = props(); render(<CardMemberPicker {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Edit Card assignees' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Unassign Taylor' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled()); expect(fetch.mock.calls[1][1].method).toBe('DELETE');
});
it('retains exact intent after response loss even when the canonical Card is unavailable or newer', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(options)).mockRejectedValueOnce(new Error('lost')).mockResolvedValueOnce(response(ack)); vi.stubGlobal('fetch', fetch);
  const p = props(); const view = render(<CardMemberPicker {...p} />); fireEvent.click(await open()); await screen.findByText('Retry assignee change');
  view.rerender(<CardMemberPicker {...p} card={undefined} />);
  expect(p.onRecoveryChange).toHaveBeenCalledWith(true); expect(screen.queryByText('Done editing assignees')).not.toBeInTheDocument();
  view.rerender(<CardMemberPicker {...p} card={{ ...card, version: 5 }} />);
  fireEvent.click(screen.getByText('Retry assignee change')); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[2][0]).toBe(fetch.mock.calls[1][0]); expect(fetch.mock.calls[2][1].method).toBe(fetch.mock.calls[1][1].method);
  expect(new Headers(fetch.mock.calls[2][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
});
it.each([401, 403, 404])('clears denied options without raw server details (%s)', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ detail: 'Private server detail' }, status))); const p = props(); render(<CardMemberPicker {...p} />);
  fireEvent.click(screen.getByRole('button', { name: 'Edit Card assignees' })); await screen.findByRole('alert'); expect(p.onRefresh).toHaveBeenCalled();
  expect(screen.queryByText('Private server detail')).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Reload member options' })).toBeDisabled();
});
it.each([
  { ...options, boardId: org }, { ...options, cardVersion: 2 },
  { ...options, items: [options.items[0], options.items[0]] },
  { ...options, nextCursor: userId }, { ...options, items: [{ ...options.items[0], assigned: 'true' }] },
])('rejects invalid scope, revision, duplicate members and cursor/flag structure', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(value))); render(<CardMemberPicker {...props()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Edit Card assignees' })); await screen.findByRole('alert'); expect(screen.queryByRole('button', { name: 'Assign Taylor' })).not.toBeInTheDocument();
});
it('retires loaded choices and a late read when the Card revision changes', async () => {
  let resolve!: (value: Response) => void;
  const fetch = vi.fn().mockResolvedValueOnce(response(options)).mockImplementationOnce(() => new Promise(r => { resolve = r; })); vi.stubGlobal('fetch', fetch);
  const p = props(); const view = render(<CardMemberPicker {...p} />); await open();
  fireEvent.click(screen.getByText('Reload member options')); await waitFor(() => expect(resolve).toBeDefined());
  view.rerender(<CardMemberPicker {...p} card={{ ...card, version: 2 }} />);
  await act(async () => resolve(response(options))); await screen.findByRole('alert');
  expect(screen.queryByRole('button', { name: 'Assign Taylor' })).not.toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(2);
});
it('fences late commands after permission loss and removes recovery state', async () => {
  let fail!: (reason: Error) => void;
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(options)).mockImplementationOnce(() => new Promise((_resolve, reject) => { fail = reject; })));
  const p = props(); const view = render(<CardMemberPicker {...p} />); fireEvent.click(await open()); await waitFor(() => expect(fail).toBeDefined());
  view.rerender(<CardMemberPicker {...p} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} />);
  await act(async () => fail(new Error('late'))); expect(screen.queryByText('Retry assignee change')).not.toBeInTheDocument(); expect(p.onRefresh).not.toHaveBeenCalled();
  expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it('requires fresh choices after a known version conflict without retrying the rejected intent', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(options)).mockResolvedValueOnce(response({ detail: 'raw detail' }, 409)));
  const p = props(); render(<CardMemberPicker {...p} />); fireEvent.click(await open()); await screen.findByRole('alert');
  expect(screen.queryByText('Retry assignee change')).not.toBeInTheDocument(); expect(screen.queryByText('raw detail')).not.toBeInTheDocument(); expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it('replaces bounded pages and restarts from the beginning', async () => {
  const ids = Array.from({ length: 50 }, (_, i) => `44444444-4444-4444-4444-${String(i + 1).padStart(12, '0')}`);
  const first = { ...options, nextCursor: ids[49], items: ids.map((userId, i) => ({ userId, displayName: `Member ${i}`, assigned: i % 2 === 0 })) };
  const fetch = vi.fn().mockResolvedValueOnce(response(first)).mockResolvedValueOnce(response({ ...options, items: [{ userId: '55555555-5555-5555-5555-555555555555', displayName: 'Next person', assigned: false }] })).mockResolvedValueOnce(response(first));
  vi.stubGlobal('fetch', fetch); render(<CardMemberPicker {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Edit Card assignees' }));
  fireEvent.click(await screen.findByText('Next members')); await screen.findByText('Next person'); expect(screen.queryByText('Member 0')).not.toBeInTheDocument();
  expect(fetch.mock.calls[1][0]).toBe(`/cards/${id}/member-options?after=${ids[49]}`);
  fireEvent.click(screen.getByText('Reload member options')); await screen.findByText('Member 0'); expect(screen.queryByText('Next person')).not.toBeInTheDocument();
});
