import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { BoardMetadataControl } from './BoardMetadataControl';
import { boardBackgroundColor } from './boardBackground';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
const board = { id: 'board', organizationId: 'org', name: 'Planning', description: 'Original description', version: 2,
  lifecycleState: 'active', backgroundType: 'COLOR', backgroundValue: 'blue' };
const snapshot: BoardSnapshot = { board, lists: [], access: { canView: true, canEdit: true, canMove: true, canAdminister: true } };
const props = { snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function open() { fireEvent.click(screen.getByRole('button', { name: 'Edit Board details' })); }
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.clearAllMocks(); });
it('saves reviewed name, description and approved background and returns focus after validated acknowledgment', async () => {
  const fetch = vi.fn().mockResolvedValue(reply({ ...board, version: 3, name: 'Updated', description: 'New description', backgroundValue: 'purple' }));
  vi.stubGlobal('fetch', fetch); render(<BoardMetadataControl {...props} />); open();
  fireEvent.change(screen.getByRole('textbox', { name: 'Board name' }), { target: { value: ' Updated ' } });
  fireEvent.change(screen.getByRole('textbox', { name: 'Board description' }), { target: { value: ' New description ' } });
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Board background' })); fireEvent.click(screen.getByRole('option', { name: 'Purple' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][0]).toBe('/boards/board'); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'Updated', description: 'New description', version: 2, backgroundType: 'COLOR', backgroundValue: 'purple' });
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Edit Board details' })).toHaveFocus());
});
it('preserves an original save and its fields after canonical data has advanced', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('Private failure')).mockResolvedValueOnce(reply({ ...board, version: 3, name: 'Updated' }));
  vi.stubGlobal('fetch', fetch); const view = render(<BoardMetadataControl {...props} />); open();
  fireEvent.change(screen.getByRole('textbox', { name: 'Board name' }), { target: { value: 'Updated' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save Board details' })); await screen.findByRole('button', { name: 'Retry this Board save' });
  view.rerender(<BoardMetadataControl {...props} snapshot={{ ...snapshot, board: { ...board, version: 4, name: 'Another name' } }} />);
  expect(screen.getByRole('textbox', { name: 'Board name' })).toHaveValue('Updated'); expect(screen.getByRole('textbox', { name: 'Board name' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Cancel Board changes' })).not.toBeInTheDocument();
  expect(props.onRecoveryChange).toHaveBeenLastCalledWith(true); fireEvent.click(screen.getByRole('button', { name: 'Retry this Board save' }));
  await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][1].body).toBe(fetch.mock.calls[1][1].body);
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'Updated', description: 'Original description', version: 2 });
  expect(screen.queryByText('Private failure')).not.toBeInTheDocument();
});
it('shows current fields and requires a renewed revision review while preserving conflicting drafts', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ ...board, version: 5, name: 'Draft name' })));
  const view = render(<BoardMetadataControl {...props} />); open(); fireEvent.change(screen.getByRole('textbox', { name: 'Board name' }), { target: { value: 'Draft name' } });
  view.rerender(<BoardMetadataControl {...props} snapshot={{ ...snapshot, board: { ...board, version: 4, name: 'Current name' } }} />);
  expect(screen.getByRole('button', { name: 'Save Board details' })).toBeDisabled();
  expect(screen.getByText('Current Board name: Current name')).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Review current Board revision' }));
  expect(screen.getByRole('textbox', { name: 'Board name' })).toHaveValue('Draft name');
  fireEvent.click(screen.getByRole('button', { name: 'Save Board details' })); await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
});
it('withdraws pending editing after fresh admission loss and ignores its late response', async () => {
  let finish!: (response: Response) => void;
  const fetch = vi.fn((_path: string, _init: RequestInit) => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal('fetch', fetch);
  const view = render(<BoardMetadataControl {...props} />); open(); fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await waitFor(() => expect(finish).toBeDefined()); view.rerender(<BoardMetadataControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} />);
  await screen.findByText('Board editing is unavailable.'); expect(fetch.mock.calls[0][1].signal?.aborted).toBe(true);
  await act(async () => finish(reply({ ...board, version: 3 })));
  expect(screen.queryByText('Board changes acknowledged. Current Board state is being checked.')).not.toBeInTheDocument();
  expect(props.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it('withholds a cross-scope acknowledgment and recovers the same reviewed save', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(reply({ ...board, version: 3, organizationId: 'foreign-org' })).mockResolvedValueOnce(reply({ ...board, version: 3 }));
  vi.stubGlobal('fetch', fetch); render(<BoardMetadataControl {...props} />); open(); fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await screen.findByRole('button', { name: 'Retry this Board save' });
  expect(screen.queryByText('Board changes acknowledged. Current Board state is being checked.')).not.toBeInTheDocument();
  expect(screen.queryByText('foreign-org')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this Board save' })); await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][1].body).toBe(fetch.mock.calls[1][1].body);
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
});
it.each([400, 409, 401, 403, 404])('handles HTTP %s with fixed conflict/admission feedback and no unresolved retry', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ detail: 'Private failure' }, status)));
  render(<BoardMetadataControl {...props} />); open(); fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await screen.findByText(status === 400 || status === 409 ? 'This save could not be applied. Check the Board and review its current revision.' : 'Board editing is unavailable.');
  expect(screen.queryByRole('button', { name: 'Retry this Board save' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private failure')).not.toBeInTheDocument();
  if (status === 400 || status === 409) expect(screen.getByRole('button', { name: 'Save Board details' })).toBeDisabled();
  else await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});
it('rejects blank names without sending a mutation and preserves historical backgrounds by omission', async () => {
  const legacy = { ...board, backgroundType: 'IMAGE', backgroundValue: 'private-object-reference' };
  const fetch = vi.fn().mockResolvedValue(reply({ ...legacy, version: 3 })); vi.stubGlobal('fetch', fetch);
  render(<BoardMetadataControl {...props} snapshot={{ ...snapshot, board: legacy }} />); open();
  fireEvent.change(screen.getByRole('textbox', { name: 'Board name' }), { target: { value: '  ' } }); fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await screen.findByText('Use a Board name with 1 to 160 characters and an available background choice.'); expect(fetch).not.toHaveBeenCalled();
  fireEvent.change(screen.getByRole('textbox', { name: 'Board name' }), { target: { value: 'Planning' } }); fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
  expect(JSON.parse(fetch.mock.calls[0][1].body)).not.toHaveProperty('backgroundValue'); expect(screen.queryByText('private-object-reference')).not.toBeInTheDocument();
});
it.each(['url(https://example.test/private)', '#ffffff', '__proto__', 'private-object-reference', null])('never renders persisted arbitrary background material %s', value => {
  expect(boardBackgroundColor({ ...board, backgroundValue: value }, 'light')).toBeUndefined();
  expect(boardBackgroundColor({ ...board, backgroundType: 'IMAGE', backgroundValue: value }, 'dark')).toBeUndefined();
});
it('maps only approved built-ins to fixed light/dark surfaces', () => {
  expect(boardBackgroundColor(board, 'light')).toBe('#eff6ff'); expect(boardBackgroundColor(board, 'dark')).toBe('#10243a');
});
async function observations(fetch: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry(); const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
  const value = JSON.parse(reports[0][1].body);
  for (const secret of [board.id, board.organizationId, board.name, board.description, 'private-object-reference', 'Private failure', 'Idempotency-Key'])
    expect(JSON.stringify(value)).not.toContain('"' + secret + '"');
  for (const event of value.events) expect(Object.keys(event).sort()).toEqual(event.durationMs === undefined
    ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return value.events as { action: string; kind: string; count: number; durationMs?: number }[];
}
it('observes original metadata recovery without retaining draft/background/request material', async () => {
  configureActivityTelemetry(true); const legacy = { ...board, backgroundType: 'IMAGE', backgroundValue: 'private-object-reference' }; let writes = 0;
  const fetch = vi.fn((path: string, _init: RequestInit) => path === '/me/activity-client-events' ? Promise.resolve(new Response(null, { status: 204 }))
    : ++writes === 1 ? Promise.reject(new Error('Private failure')) : Promise.resolve(reply({ ...legacy, version: 3 })));
  vi.stubGlobal('fetch', fetch); const view = render(<BoardMetadataControl {...props} snapshot={{ ...snapshot, board: legacy }} />); open();
  fireEvent.click(screen.getByRole('button', { name: 'Save Board details' })); await screen.findByRole('button', { name: 'Retry this Board save' });
  view.rerender(<BoardMetadataControl {...props} snapshot={{ ...snapshot, board: { ...legacy, version: 4, name: 'Another Board name' } }} />);
  fireEvent.click(screen.getByRole('button', { name: 'Retry this Board save' })); await screen.findByText('Board changes acknowledged. Current Board state is being checked.');
  const events = await observations(fetch);
  for (const kind of ['open', 'use', 'retry', 'exception']) expect(events).toContainEqual({ action: 'board_metadata_update', kind, count: 1 });
  for (const kind of ['success', 'failure']) expect(events).toContainEqual({ action: 'board_metadata_update', kind, count: 1, durationMs: expect.any(Number) });
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
});
it.each([409, 403])('observes HTTP %s as failure without a successful mutation or private diagnostics', async status => {
  configureActivityTelemetry(true);
  const fetch = vi.fn((path: string, _init: RequestInit) => Promise.resolve(path === '/me/activity-client-events'
    ? new Response(null, { status: 204 }) : reply({ detail: 'Private failure' }, status)));
  vi.stubGlobal('fetch', fetch); render(<BoardMetadataControl {...props} />); open(); fireEvent.click(screen.getByRole('button', { name: 'Save Board details' }));
  await screen.findByText(status === 409 ? 'This save could not be applied. Check the Board and review its current revision.' : 'Board editing is unavailable.');
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: 'board_metadata_update', kind: 'failure', count: 1, durationMs: expect.any(Number) });
  expect(events.some(event => event.kind === 'success')).toBe(false);
  expect(events.some(event => event.kind === 'conflict')).toBe(status === 409);
});
