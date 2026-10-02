import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { LabelCreateControl } from './LabelCreateControl';
import type { BoardSnapshot } from '../../api/workManagement';
const board = '11111111-1111-1111-1111-111111111111', org = '22222222-2222-2222-2222-222222222222';
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' }, lists: [], access: { canView: true, canEdit: true, canAdminister: true, canMove: true } };
const props = () => ({ snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() });
const ack = { id: '33333333-3333-3333-3333-333333333333', boardId: board, organizationId: org, name: 'Priority', color: 'green', version: 1, deleted: false, rank: '500000000000000000000000000000' };
const response = (value: unknown = ack, status = 201) => new Response(JSON.stringify(value), { status });
function open(name = ' Priority ') { fireEvent.click(screen.getByRole('button', { name: 'Create label' })); fireEvent.change(screen.getByLabelText('Label name (optional)'), { target: { value: name } }); }
afterEach(() => vi.unstubAllGlobals());
it('creates a normalized label with a UUID key and returns focus after confirmation', async () => {
  const fetch = vi.fn().mockResolvedValue(response()); vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelCreateControl {...p} />);
  open(); fireEvent.click(screen.getByRole('button', { name: 'Create' }));
  await waitFor(() => expect(p.onRefresh).toHaveBeenCalledTimes(1));
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'Priority', color: 'green' });
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(p.onReturnFocus).toHaveBeenCalled());
});
it('allows an unnamed label with a named color', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ ...ack, name: '' }))); const p = props(); render(<LabelCreateControl {...p} />);
  open(''); fireEvent.click(screen.getByRole('button', { name: 'Create' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
});
it('retains the exact key and body after a lost acknowledgement', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('lost')).mockResolvedValueOnce(response()); vi.stubGlobal('fetch', fetch); const p = props(); render(<LabelCreateControl {...p} />);
  open(); fireEvent.click(screen.getByRole('button', { name: 'Create' }));
  const retry = await screen.findByRole('button', { name: 'Retry label creation' });
  expect(screen.queryByLabelText('Label name (optional)')).not.toBeInTheDocument(); expect(screen.getByText('Cancel')).toBeDisabled();
  expect(p.onRecoveryChange).toHaveBeenCalledWith(true); fireEvent.click(retry);
  await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(fetch.mock.calls[0][1].body).toBe(fetch.mock.calls[1][1].body);
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
});
it('treats a mismatched successful response as an unresolved creation', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ ...ack, boardId: org }))); const p = props(); render(<LabelCreateControl {...p} />);
  open(); fireEvent.click(screen.getByRole('button', { name: 'Create' })); await screen.findByText('Retry label creation'); expect(p.onRefresh).not.toHaveBeenCalled();
});
it.each([401, 403, 404])('clears the command and refreshes access after denial %s', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ detail: 'Private server message' }, status))); const p = props(); render(<LabelCreateControl {...p} />);
  open(); fireEvent.click(screen.getByRole('button', { name: 'Create' })); await waitFor(() => expect(p.onRefresh).toHaveBeenCalled());
  expect(screen.queryByText('Retry label creation')).not.toBeInTheDocument(); expect(screen.queryByText('Private server message')).not.toBeInTheDocument();
});
it('ignores late failure after permission loss and can reopen when access returns', async () => {
  let reject!: (reason: Error) => void; vi.stubGlobal('fetch', vi.fn().mockReturnValue(new Promise((_resolve, failure) => { reject = failure; })));
  const p = props(); const view = render(<LabelCreateControl {...p} />); open(); fireEvent.click(screen.getByRole('button', { name: 'Create' }));
  await waitFor(() => expect(reject).toBeDefined());
  view.rerender(<LabelCreateControl {...p} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} />);
  await act(async () => reject(new Error('late failure')));
  expect(screen.queryByText('Retry label creation')).not.toBeInTheDocument();
  view.rerender(<LabelCreateControl {...p} />); expect(await screen.findByRole('button', { name: 'Create label' })).toBeEnabled();
});
