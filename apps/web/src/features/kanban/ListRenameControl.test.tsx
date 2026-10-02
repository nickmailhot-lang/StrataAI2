import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { ListRenameControl } from './ListRenameControl';

const list = { id: 'list', name: 'Planning', rank: '500000000000000000000000000000', version: 1, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [{ list, cards: [] }] };
const ack = { ...list, name: 'Renamed', version: 2, boardId: 'board', organizationId: 'org' };
const props = { list, snapshot, disabled: false, onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function open(name = 'Renamed') {
  fireEvent.click(screen.getByRole('button', { name: 'Rename Planning list' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'New list name' }), { target: { value: name } });
}
function save() { fireEvent.click(screen.getByRole('button', { name: 'Save list name' })); }
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); vi.clearAllMocks(); });

it('submits only a reviewed name/version with a key and accepts the exact scoped acknowledgment', async () => {
  const fetch = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetch);
  render(<ListRenameControl {...props} />); open('  Renamed  '); save();
  await screen.findByText('List rename acknowledged. Checking current list.');
  expect(fetch.mock.calls[0][0]).toBe('/lists/list');
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'Renamed', version: 1 });
  expect(fetch.mock.calls[0][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(props.onRefresh).toHaveBeenCalledOnce();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Rename Planning list' })).toHaveFocus());
});
it('keeps a lost acknowledgment bound to its original name/version/key after newer canonical data', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('Lost')).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetch);
  const view = render(<ListRenameControl {...props} />); open(); save();
  await screen.findByRole('button', { name: 'Retry this rename' });
  view.rerender(<ListRenameControl {...props} list={{ ...list, name: 'Newer name', version: 8 }} />);
  expect(screen.getByRole('textbox', { name: 'New list name' })).toHaveValue('Renamed');
  expect(screen.getByRole('textbox', { name: 'New list name' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Cancel rename' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this rename' }));
  await screen.findByText('List rename acknowledged. Checking current list.');
  expect(fetch.mock.calls[1][1].body).toBe(fetch.mock.calls[0][1].body);
  expect(fetch.mock.calls[1][1].headers.get('Idempotency-Key')).toBe(fetch.mock.calls[0][1].headers.get('Idempotency-Key'));
});
it('preserves a dirty draft on a live revision and requires explicit discard before saving again', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  const view = render(<ListRenameControl {...props} />); open();
  view.rerender(<ListRenameControl {...props} list={{ ...list, name: 'Canonical name', version: 2 }} />);
  expect(screen.getByRole('textbox', { name: 'New list name' })).toHaveValue('Renamed');
  expect(screen.getByRole('button', { name: 'Save list name' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Discard draft and use current list' }));
  expect(screen.getByRole('textbox', { name: 'New list name' })).toHaveValue('Canonical name');
  expect(screen.getByRole('button', { name: 'Save list name' })).toBeEnabled(); expect(fetch).not.toHaveBeenCalled();
});
it.each([
  { ...ack, id: 'other' }, { ...ack, organizationId: 'other' }, { ...ack, boardId: 'other' },
  { ...ack, name: 'Different' }, { ...ack, rank: 'different' }, { ...ack, version: 3 },
  { ...ack, lifecycleState: 'archived' }, null,
])('rejects an acknowledgment whose identity, scope, name, rank, revision or lifecycle differs: %j', async value => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(value)));
  render(<ListRenameControl {...props} />); open(); save();
  await screen.findByRole('button', { name: 'Retry this rename' });
  expect(screen.queryByText('List rename acknowledged. Checking current list.')).not.toBeInTheDocument();
});
it('bounds a stalled request and preserves the original retry intent', async () => {
  vi.useFakeTimers(); const fetch = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => new Promise<Response>(() => {})); vi.stubGlobal('fetch', fetch);
  render(<ListRenameControl {...props} />); open(); save();
  await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  expect(screen.getByRole('button', { name: 'Retry this rename' })).toBeEnabled();
  expect(fetch.mock.calls[0][1]?.signal?.aborted).toBe(true);
  expect(props.onBusyChange).toHaveBeenLastCalledWith(false);
});
it('handles conflicts without displaying private server titles or replacing the draft', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ title: 'private SQL detail', code: 'version_conflict' }, 409)));
  render(<ListRenameControl {...props} />); open(); save();
  await screen.findByText('This rename could not be applied. Check the current list before reviewing another save.');
  expect(screen.getByRole('textbox', { name: 'New list name' })).toHaveValue('Renamed');
  expect(screen.getByRole('button', { name: 'Save list name' })).toBeDisabled();
  expect(screen.queryByText('private SQL detail')).not.toBeInTheDocument();
});
it('hides the action for viewers and removes the reviewed draft on denied persistence', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({}, 404)));
  const view = render(<ListRenameControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canMove: false } }} />);
  expect(screen.queryByRole('button', { name: 'Rename Planning list' })).not.toBeInTheDocument();
  view.rerender(<ListRenameControl {...props} />); open(); save();
  await screen.findByText('This list or action is unavailable.');
  expect(screen.queryByRole('textbox', { name: 'New list name' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Rename Planning list' })).not.toBeInTheDocument();
});
it('rejects a blank name locally and aborts a pending rename when its scope unmounts', async () => {
  const fetch = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => new Promise<Response>(() => {})); vi.stubGlobal('fetch', fetch);
  const view = render(<ListRenameControl {...props} />); open('   '); save();
  expect(screen.getByText('Use a list name with 1 to 160 characters.')).toBeInTheDocument(); expect(fetch).not.toHaveBeenCalled();
  fireEvent.change(screen.getByRole('textbox', { name: 'New list name' }), { target: { value: 'Renamed' } }); save();
  view.unmount(); expect(fetch.mock.calls[0][1]?.signal?.aborted).toBe(true);
});
