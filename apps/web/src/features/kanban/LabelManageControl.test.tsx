import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { LabelManageControl } from './LabelManageControl';
import type { BoardSnapshot } from '../../api/workManagement';
const board = '11111111-1111-1111-1111-111111111111', org = '22222222-2222-2222-2222-222222222222';
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' }, lists: [], access: { canView: true, canEdit: true, canAdminister: true, canMove: true } };
const original = { id: '33333333-3333-3333-3333-333333333333', boardId: board, organizationId: org, name: 'Priority', color: 'green', version: 4, deleted: false, rank: '500000000000000000000000000000' };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const directory = (changes: Record<string, unknown> = {}) => response({ boardId: board, organizationId: org, items: [original], nextCursor: null, canEdit: true, canDelete: true, ...changes });
const props = () => ({ snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() });
async function open() {
  fireEvent.click(screen.getByRole('button', { name: 'Manage labels' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Edit Priority (green)' }));
}
afterEach(() => vi.unstubAllGlobals());
it('renames and recolors at the read revision without submitting a raw rank', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(directory()).mockResolvedValueOnce(response({ ...original, name: 'Urgent', color: 'blue', version: 5 }));
  vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelManageControl {...p} />); await open();
  fireEvent.change(screen.getByLabelText('Label name (optional)'), { target: { value: ' Urgent ' } });
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Label color' })); fireEvent.click(await screen.findByRole('option', { name: 'Blue' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save label' }));
  await waitFor(() => expect(p.onRefresh).toHaveBeenCalledTimes(1));
  expect(fetch.mock.calls[1][0]).toBe(`/labels/${original.id}`); expect(fetch.mock.calls[1][1].method).toBe('PATCH');
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ name: 'Urgent', color: 'blue', version: 4 });
  expect(screen.queryByLabelText('Label name (optional)')).not.toBeInTheDocument();
});
it('moves to the end with saved metadata and original revision', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(directory()).mockResolvedValueOnce(response({ ...original, rank: '750000000000000000000000000000', version: 5 }));
  vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelManageControl {...p} />); await open();
  fireEvent.change(screen.getByLabelText('Label name (optional)'), { target: { value: 'Unsaved draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Move label' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[1][0]).toBe(`/labels/${original.id}/move`);
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ beforeLabelId: null, version: 4 });
});
it('retains the selected label while paging to a different ordering destination', async () => {
  const others = Array.from({ length: 49 }, (_, i) => ({ ...original, id: `55555555-5555-5555-5555-${String(i + 1).padStart(12, '0')}`, name: `Other ${i}` }));
  const anchor = { ...original, id: '66666666-6666-6666-6666-666666666666', name: 'Destination', rank: '750000000000000000000000000000' };
  const fetch = vi.fn().mockResolvedValueOnce(directory({ items: [original, ...others], nextCursor: others.at(-1)!.id }))
    .mockResolvedValueOnce(directory({ items: [anchor] }))
    .mockResolvedValueOnce(response({ ...original, version: 5, rank: '625000000000000000000000000000' }));
  vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelManageControl {...p} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Next destination labels' }));
  await screen.findByRole('button', { name: 'Edit Destination (green)' });
  expect(screen.getByLabelText('Label name (optional)')).toHaveValue('Priority');
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Move label before' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Destination (green)' }));
  fireEvent.click(screen.getByRole('button', { name: 'Move label' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[1][0]).toBe(`/boards/${board}/labels?after=${others.at(-1)!.id}`);
  expect(fetch.mock.calls[2][0]).toBe(`/labels/${original.id}/move`);
  expect(JSON.parse(fetch.mock.calls[2][1].body)).toEqual({ beforeLabelId: anchor.id, version: 4 });
});
it('requires explicit consent before deleting the Board definition', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(directory()).mockResolvedValueOnce(response({ ...original, version: 5, deleted: true }));
  vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelManageControl {...p} />); await open();
  expect(screen.getByRole('button', { name: 'Delete label' })).toBeDisabled();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm removal from all Cards' }));
  fireEvent.click(screen.getByRole('button', { name: 'Delete label' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[1][0]).toBe(`/labels/${original.id}?version=4&confirmed=true`);
  expect(fetch.mock.calls[1][1].method).toBe('DELETE');
});
it('keeps exactly the same URL, body and key after a lost acknowledgement', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(directory()).mockRejectedValueOnce(new Error('lost')).mockResolvedValueOnce(response({ ...original, name: 'Urgent', version: 5 }));
  vi.stubGlobal('fetch', fetch); const p = props(); const view = render(<LabelManageControl {...p} />); await open();
  fireEvent.change(screen.getByLabelText('Label name (optional)'), { target: { value: 'Urgent' } }); fireEvent.click(screen.getByRole('button', { name: 'Save label' }));
  await screen.findByRole('button', { name: 'Retry label change' }); expect(screen.getByRole('button', { name: 'Done' })).toBeDisabled();
  expect(screen.queryByLabelText('Label name (optional)')).not.toBeInTheDocument();
  view.rerender(<LabelManageControl {...p} snapshot={{ ...snapshot, lists: [] }} />);
  fireEvent.click(screen.getByRole('button', { name: 'Retry label change' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[1][0]).toBe(fetch.mock.calls[2][0]); expect(fetch.mock.calls[1][1].body).toBe(fetch.mock.calls[2][1].body);
  expect(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[2][1].headers).get('Idempotency-Key'));
});
it('discards stale choices after a conflict and requires a new read', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(directory()).mockResolvedValueOnce(response({ detail: 'Private SQL' }, 409));
  vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelManageControl {...p} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Save label' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(screen.queryByText('Private SQL')).not.toBeInTheDocument(); expect(screen.queryByText('Retry label change')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Label name (optional)')).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Done' })).toBeEnabled();
});
it('treats a mismatched success as an uncertain outcome', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(directory()).mockResolvedValueOnce(response({ ...original, version: 5, boardId: org })));
  const p = props(); render(<LabelManageControl {...p} />); await open(); fireEvent.click(screen.getByRole('button', { name: 'Save label' }));
  await screen.findByRole('button', { name: 'Retry label change' }); expect(p.onRefresh).not.toHaveBeenCalled();
});
it('withholds deletion if the current directory forbids it', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(directory({ canDelete: false }))); render(<LabelManageControl {...props()} />); await open();
  expect(screen.queryByRole('button', { name: 'Delete label' })).not.toBeInTheDocument();
});
it('returns focus only after the closing dialog and Board refresh finish', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(directory())); const p = props();
  const view = render(<LabelManageControl {...p} />); await open();
  view.rerender(<LabelManageControl {...p} disabled />); fireEvent.click(screen.getByRole('button', { name: 'Done' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(p.onReturnFocus).not.toHaveBeenCalled();
  view.rerender(<LabelManageControl {...p} />); await waitFor(() => expect(p.onReturnFocus).toHaveBeenCalledTimes(1));
});
it('clears pending deletion and fences late failures after administrator demotion', async () => {
  let reject!: (reason: Error) => void;
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(directory()).mockReturnValueOnce(new Promise((_resolve, failure) => { reject = failure; })));
  const p = props(); const view = render(<LabelManageControl {...p} />); await open();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm removal from all Cards' })); fireEvent.click(screen.getByRole('button', { name: 'Delete label' }));
  await waitFor(() => expect(reject).toBeDefined());
  view.rerender(<LabelManageControl {...p} snapshot={{ ...snapshot, access: { ...snapshot.access, canAdminister: false } }} />);
  await act(async () => reject(new Error('late failure')));
  expect(screen.queryByRole('button', { name: 'Retry label change' })).not.toBeInTheDocument(); expect(screen.queryByText('Delete label')).not.toBeInTheDocument();
});
it('rejects another Board directory without exposing its labels', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(directory({ boardId: org }))); render(<LabelManageControl {...props()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Manage labels' })); await screen.findByText('Unable to load current labels. Reload labels or refresh the Board.');
  expect(screen.queryByText('Edit Priority (green)')).not.toBeInTheDocument();
});
it.each([401, 403, 404])('clears the editing directory after a denied write %s', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(directory()).mockResolvedValueOnce(response({ detail: 'Private denial' }, status)));
  const p = props(); render(<LabelManageControl {...p} />); await open(); fireEvent.click(screen.getByRole('button', { name: 'Save label' }));
  await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(screen.queryByText('Retry label change')).not.toBeInTheDocument(); expect(screen.queryByText('Private denial')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});
