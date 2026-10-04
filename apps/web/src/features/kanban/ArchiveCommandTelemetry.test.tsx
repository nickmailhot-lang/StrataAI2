import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { ListArchiveControl } from './ListArchiveControl';
import { CardArchiveControl } from './CardArchiveControl';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';

const org = '10000000-0000-4000-8000-000000000001', board = '10000000-0000-4000-8000-000000000002';
const list = { id: '20000000-0000-4000-8000-000000000001', name: 'Private planning', rank: '500', version: 1, lifecycleState: 'active' };
const card = { id: '30000000-0000-4000-8000-000000000001', title: 'Private budget', description: 'Private body', rank: '500', version: 1 };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Private Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canMove: true, canEdit: true, canAdminister: true }, lists: [{ list, cards: [card] }] };
const kinds = ['list', 'card'] as const;
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function fixture(kind: typeof kinds[number]) {
  const callbacks = { onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn(), onAcknowledged: vi.fn() };
  const ack = { ...(kind === 'list' ? list : card), organizationId: org, boardId: board, listId: list.id, version: 2, lifecycleState: 'archived' };
  const control = (current = snapshot, currentCard: typeof card | null = card) => kind === 'list'
    ? <ListArchiveControl snapshot={current} disabled={false} unavailableListIds={new Set()} {...callbacks} />
    : <CardArchiveControl cardId={card.id} card={currentCard ?? undefined} snapshot={current} disabled={false} {...callbacks} />;
  function review() {
    fireEvent.click(screen.getByRole('button', { name: kind === 'list' ? 'Archive a list' : 'Archive Card' }));
    if (kind === 'list') {
      fireEvent.mouseDown(screen.getByRole('combobox', { name: 'List to archive' }));
      fireEvent.click(screen.getByRole('option', { name: list.name }));
    }
  }
  const confirm = kind === 'list' ? 'Confirm archive' : 'Confirm Card archive';
  const retry = kind === 'list' ? 'Retry this archive' : 'Retry this Card archive';
  return { ack, control, review, confirm, retry, callbacks };
}
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
async function observations(fetch: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetch.mock.calls.filter(call => call[0] === '/me/activity-client-events');
  expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1]?.body as string);
  for (const secret of [org, board, list.id, card.id, list.name, card.title, card.description, 'Private failure', 'Idempotency-Key'])
    expect(JSON.stringify(body)).not.toContain(secret);
  for (const event of body.events) expect(Object.keys(event).sort()).toEqual(
    event.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  return body.events as { action: string; kind: string; count: number; durationMs?: number }[];
}
it.each(kinds)('reports %s archive recovery after canonical removal without private command data', async kind => {
  configureActivityTelemetry(true); const f = fixture(kind); let writes = 0;
  const fetch = vi.fn((path: string, _init?: RequestInit) => path === '/me/activity-client-events' ? Promise.resolve(new Response(null, { status: 204 }))
    : ++writes === 1 ? Promise.reject(new Error('Private failure')) : Promise.resolve(reply(f.ack)));
  vi.stubGlobal('fetch', fetch); const view = render(f.control()); f.review();
  fireEvent.click(screen.getByRole('button', { name: f.confirm })); await screen.findByRole('button', { name: f.retry });
  view.rerender(f.control({ ...snapshot, lists: [] }, null));
  fireEvent.click(screen.getByRole('button', { name: f.retry }));
  await screen.findByText(`${kind === 'list' ? 'List' : 'Card'} archive acknowledged. Current Board state is being checked.`);
  const events = await observations(fetch);
  for (const event of ['open', 'use', 'retry', 'exception']) expect(events).toContainEqual({ action: `${kind}_archive`, kind: event, count: 1 });
  for (const event of ['success', 'failure']) expect(events).toContainEqual({ action: `${kind}_archive`, kind: event, count: 1, durationMs: expect.any(Number) });
  const commands = fetch.mock.calls.filter(call => call[0] !== '/me/activity-client-events');
  expect(commands[0][1]?.body).toBe(commands[1][1]?.body);
  expect(new Headers(commands[0][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1]?.headers).get('Idempotency-Key'));
});
it.each(kinds.flatMap(kind => [409, 404].map(status => ({ kind, status }))))('reports $kind archive $status as failure', async ({ kind, status }) => {
  configureActivityTelemetry(true); const f = fixture(kind);
  const fetch = vi.fn((path: string, _init?: RequestInit) => Promise.resolve(path === '/me/activity-client-events'
    ? new Response(null, { status: 204 }) : reply({ detail: 'Private failure' }, status)));
  vi.stubGlobal('fetch', fetch); render(f.control()); f.review(); fireEvent.click(screen.getByRole('button', { name: f.confirm }));
  await waitFor(() => expect(f.callbacks.onRefresh).toHaveBeenCalled());
  const events = await observations(fetch);
  expect(events).toContainEqual({ action: `${kind}_archive`, kind: 'failure', count: 1, durationMs: expect.any(Number) });
  expect(events.some(event => event.kind === 'success')).toBe(false);
  expect(events.some(event => event.kind === 'conflict')).toBe(status === 409);
});
it.each(kinds)('withdraws pending %s archive after fresh permission loss and ignores its late acknowledgment', async kind => {
  configureActivityTelemetry(true); const f = fixture(kind); let finish!: (value: Response) => void;
  const fetch = vi.fn((path: string, _init?: RequestInit) => path === '/me/activity-client-events' ? Promise.resolve(new Response(null, { status: 204 }))
    : new Promise<Response>(resolve => { finish = resolve; }));
  vi.stubGlobal('fetch', fetch); const view = render(f.control()); f.review(); fireEvent.click(screen.getByRole('button', { name: f.confirm }));
  const signal = fetch.mock.calls[0][1]?.signal as AbortSignal;
  view.rerender(f.control({ ...snapshot, access: { canView: true, canMove: false, canEdit: false, canAdminister: false } }));
  expect(signal.aborted).toBe(true);
  await waitFor(() => expect(screen.queryByRole('button', { name: f.retry })).not.toBeInTheDocument());
  await act(async () => finish(reply(f.ack)));
  expect(screen.queryByText(`${kind === 'list' ? 'List' : 'Card'} archive acknowledged. Current Board state is being checked.`)).not.toBeInTheDocument();
  expect(f.callbacks.onBusyChange).toHaveBeenLastCalledWith(false);
  expect(f.callbacks.onRecoveryChange).toHaveBeenLastCalledWith(false);
  const events = await observations(fetch);
  expect(events.some(event => event.kind === 'success' || event.kind === 'exception')).toBe(false);
  expect(fetch.mock.calls.filter(call => call[0] !== '/me/activity-client-events')).toHaveLength(1);
});
