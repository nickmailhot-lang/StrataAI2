import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { ListArchiveControl } from './ListArchiveControl';
const list = { id: 'list', name: 'Planning', rank: '500000000000000000000000000000', version: 1, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canMove: true, canEdit: true, canAdminister: true }, lists: [{ list, cards: [] }] };
const props = { snapshot, disabled: false, unavailableListIds: new Set<string>(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() };
const ack = { ...list, organizationId: 'org', boardId: 'board', lifecycleState: 'archived', version: 2 };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function choose() {
  fireEvent.click(screen.getByRole('button', { name: 'Archive a list' }));
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'List to archive' }));
  fireEvent.click(screen.getByRole('option', { name: 'Planning' }));
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });
it('requires explicit selection/review and sends only the reviewed version with a new key', async () => {
  const fetch = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetch);
  render(<ListArchiveControl {...props} />); choose();
  expect(screen.getByText(/All contained cards remain associated/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await screen.findByText('List archive acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][0]).toBe('/lists/list/archive'); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ version: 1 });
  expect(fetch.mock.calls[0][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Archive a list' })).toHaveFocus());
});
it('keeps the original retry after the successfully archived column disappears from canonical data', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('Lost')).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetch);
  const view = render(<ListArchiveControl {...props} />); choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await screen.findByRole('button', { name: 'Retry this archive' });
  view.rerender(<ListArchiveControl {...props} snapshot={{ ...snapshot, lists: [] }} />);
  expect(screen.getByRole('button', { name: 'Retry this archive' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: 'Cancel archive' })).not.toBeInTheDocument();
  expect(props.onRecoveryChange).toHaveBeenLastCalledWith(true);
  fireEvent.click(screen.getByRole('button', { name: 'Retry this archive' }));
  await screen.findByText('List archive acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[1][1].body).toBe(fetch.mock.calls[0][1].body);
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetch.mock.calls[0][1].headers.get('Idempotency-Key'));
  await waitFor(() => expect(props.onReturnFocus).toHaveBeenCalled());
});
it('blocks changed consent until a fresh explicit review', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  const view = render(<ListArchiveControl {...props} />); choose();
  view.rerender(<ListArchiveControl {...props} snapshot={{ ...snapshot, lists: [{ list: { ...list, version: 2 }, cards: [] }] }} />);
  expect(screen.getByRole('button', { name: 'Confirm archive' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Review current list for archive' }));
  expect(screen.getByRole('button', { name: 'Confirm archive' })).toBeEnabled(); expect(fetch).not.toHaveBeenCalled();
});
it.each([{ ...ack, organizationId: 'other' }, { ...ack, boardId: 'other' }, { ...ack, name: 'Other' },
  { ...ack, rank: 'other' }, { ...ack, version: 3 }, { ...ack, lifecycleState: 'active' }, null])('rejects wrong acknowledgment fields: %j', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(value)));
  render(<ListArchiveControl {...props} />); choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await screen.findByRole('button', { name: 'Retry this archive' });
  expect(screen.queryByText('List archive acknowledged. Current Board state is being checked.')).not.toBeInTheDocument();
});
it('withholds administration from contributors and preserves other unresolved operations', () => {
  const view = render(<ListArchiveControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canAdminister: false } }} />);
  expect(screen.queryByRole('button', { name: 'Archive a list' })).not.toBeInTheDocument();
  view.rerender(<ListArchiveControl {...props} />); choose();
  view.rerender(<ListArchiveControl {...props} unavailableListIds={new Set(['list'])} />);
  expect(screen.getByRole('button', { name: 'Confirm archive' })).toBeDisabled();
});
it('bounds a stalled archive and retains recovery without changing the original request', async () => {
  vi.useFakeTimers(); const fetch = vi.fn((_path: RequestInfo | URL, _init?: RequestInit) => new Promise<Response>(() => {})); vi.stubGlobal('fetch', fetch);
  render(<ListArchiveControl {...props} />); choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  expect(screen.getByRole('button', { name: 'Retry this archive' })).toBeEnabled();
  expect(fetch.mock.calls[0][1]?.signal?.aborted).toBe(true); expect(props.onBusyChange).toHaveBeenLastCalledWith(false);
});
it('clears reviewed content after denial and never exposes private server titles', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ title: 'private SQL' }, 404)));
  render(<ListArchiveControl {...props} />); choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await screen.findByText('This List or administration action is unavailable.');
  expect(screen.queryByText('private SQL')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});
