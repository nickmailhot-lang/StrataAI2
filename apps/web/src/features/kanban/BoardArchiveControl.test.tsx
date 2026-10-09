import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { BoardArchiveControl } from './BoardArchiveControl';
import type { BoardSnapshot } from '../../api/workManagement';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
const board = { id: '20000000-0000-4000-8000-000000000001', organizationId: '10000000-0000-4000-8000-000000000001',
  name: 'Private planning', description: null, version: 2, lifecycleState: 'active' };
const snapshot: BoardSnapshot = { board, lists: [], access: { canView: true, canAdminister: true, canEdit: true, canMove: true } };
const props = { snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() };
const ack = { ...board, version: 3, lifecycleState: 'archived' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const view = (value = props) => <MemoryRouter><BoardArchiveControl {...value} /></MemoryRouter>;
function submit() { fireEvent.click(screen.getByRole('button', { name: 'Archive Board' })); fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' })); }
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.clearAllMocks(); });
it('reviews the Board impact and sends its version, then exposes authoritative archive management', async () => {
  const fetch = vi.fn().mockResolvedValue(reply(ack)); vi.stubGlobal('fetch', fetch); const mounted = render(view());
  fireEvent.click(screen.getByRole('button', { name: 'Archive Board' }));
  expect(screen.getByText(/Its Lists and Cards remain associated/)).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await screen.findByText('Board archive acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][0]).toBe(`/boards/${board.id}/archive`);
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ version: 2 });
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(props.onRefresh).toHaveBeenCalled();
  mounted.rerender(view({ ...props, snapshot: { ...snapshot, board: ack, access: { ...snapshot.access, canEdit: false, canMove: false } } }));
  expect(screen.queryByRole('button', { name: 'Archive Board' })).not.toBeInTheDocument();
  expect(await screen.findByRole('link', { name: 'Manage archived Boards' })).toHaveAttribute('href', `/app/${board.organizationId}/archived-boards`);
});
it('recovers the same archive after a lost response makes the Board read-only, with private-free observations', async () => {
  configureActivityTelemetry(true); let writes = 0;
  const fetch = vi.fn((path: string, _init: RequestInit) => path === '/me/activity-client-events' ? Promise.resolve(new Response(null, { status: 204 }))
    : ++writes === 1 ? Promise.reject(new Error('Private failure')) : Promise.resolve(reply(ack)));
  vi.stubGlobal('fetch', fetch); const mounted = render(view()); submit();
  await screen.findByRole('button', { name: 'Retry this archive' });
  mounted.rerender(view({ ...props, snapshot: { ...snapshot, board: ack, access: { ...snapshot.access, canEdit: false, canMove: false } } }));
  expect(screen.getByRole('button', { name: 'Retry this archive' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: 'Cancel archive' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry this archive' }));
  await screen.findByText('Board archive acknowledged. Current Board state is being checked.');
  expect(fetch.mock.calls[0][1].body).toBe(fetch.mock.calls[1][1].body);
  expect(new Headers(fetch.mock.calls[0][1].headers).get('Idempotency-Key')).toBe(new Headers(fetch.mock.calls[1][1].headers).get('Idempotency-Key'));
  await waitFor(() => expect(props.onReturnFocus).toHaveBeenCalled());
  await flushActivityTelemetry(); const report = JSON.parse(fetch.mock.calls.find(call => call[0] === '/me/activity-client-events')![1].body as string);
  for (const secret of [board.id, board.organizationId, board.name, 'Private failure', 'Idempotency-Key']) expect(JSON.stringify(report)).not.toContain(secret);
  for (const event of report.events) expect(Object.keys(event).sort()).toEqual(event.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  for (const kind of ['open', 'use', 'retry', 'exception']) expect(report.events).toContainEqual({ action: 'board_archive', kind, count: 1 });
  for (const kind of ['success', 'failure']) expect(report.events).toContainEqual({ action: 'board_archive', kind, count: 1, durationMs: expect.any(Number) });
});
it('requires a fresh review after canonical revision changes or a conflict', async () => {
  const fetch = vi.fn().mockResolvedValue(reply({ detail: 'Private failure' }, 409)); vi.stubGlobal('fetch', fetch);
  const mounted = render(view()); fireEvent.click(screen.getByRole('button', { name: 'Archive Board' }));
  mounted.rerender(view({ ...props, snapshot: { ...snapshot, board: { ...board, version: 4, name: 'Updated planning' } } }));
  expect(screen.getByRole('button', { name: 'Confirm archive' })).toBeDisabled(); expect(fetch).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Review current Board for archive' })); fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }));
  await screen.findByText('This archive could not be applied. Check the Board and review its current state.');
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ version: 4 });
  expect(screen.getByRole('button', { name: 'Confirm archive' })).toBeDisabled();
  expect(screen.queryByText('Private failure')).not.toBeInTheDocument();
});
it.each([401, 403, 404])('retires denied archive intent for HTTP %s without leaking details', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ detail: 'Private failure' }, status))); render(view()); submit();
  await screen.findByText('Board administration is unavailable.');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByRole('button', { name: 'Retry this archive' })).not.toBeInTheDocument();
});
it('withdraws a pending command after fresh administrator loss and ignores its late acknowledgment', async () => {
  let finish!: (value: Response) => void;
  const fetch = vi.fn((_path: string, _init: RequestInit) => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal('fetch', fetch);
  const mounted = render(view()); submit(); await waitFor(() => expect(finish).toBeDefined());
  mounted.rerender(view({ ...props, snapshot: { ...snapshot, access: { ...snapshot.access, canAdminister: false } } }));
  await screen.findByText('Board administration is unavailable.'); expect(fetch.mock.calls[0][1].signal?.aborted).toBe(true);
  await act(async () => finish(reply(ack))); expect(screen.queryByText('Board archive acknowledged. Current Board state is being checked.')).not.toBeInTheDocument();
  expect(props.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it('cannot start a new archive on an archived Board or without a reviewed revision', () => {
  const mounted = render(view({ ...props, snapshot: { ...snapshot, board: ack } }));
  expect(screen.queryByRole('button', { name: 'Archive Board' })).not.toBeInTheDocument();
  mounted.rerender(view({ ...props, snapshot: { ...snapshot, board: { ...board, version: undefined } } }));
  expect(screen.queryByRole('button', { name: 'Archive Board' })).not.toBeInTheDocument();
});

it('preserves a chosen destination while delayed archive focus awaits the current Board read', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(ack)));
  const returnFocus = vi.fn(() => screen.getByRole('button', { name: 'Refresh destination' }).focus());
  const renderControl = (disabled: boolean, archived: boolean) => <MemoryRouter>
    <button>Refresh destination</button><a href="/another">Chosen destination</a>
    <BoardArchiveControl {...props} disabled={disabled} onReturnFocus={returnFocus}
      snapshot={archived ? { ...snapshot, board: ack, access: { ...snapshot.access, canEdit: false, canMove: false } } : snapshot} />
  </MemoryRouter>;
  const mounted = render(renderControl(false, false)); submit();
  await screen.findByText('Board archive acknowledged. Current Board state is being checked.');
  mounted.rerender(renderControl(true, true));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  const destination = screen.getByRole('link', { name: 'Chosen destination' }); destination.focus();
  expect(destination).toHaveFocus(); returnFocus.mockClear();
  mounted.rerender(renderControl(false, true));
  expect(destination).toHaveFocus(); expect(returnFocus).not.toHaveBeenCalled();
  expect(screen.getByRole('link', { name: 'Manage archived Boards' })).toBeEnabled();
});
