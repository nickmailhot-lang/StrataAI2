import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Button, Dialog } from '@mui/material';
import { CardCommentsControl } from './CardCommentsControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const row = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, authorId: profile.id,
  content: 'Literal <script>🙂', createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z',
  version: 1, editedAt: null, deletedAt: null, deletedBy: null };
const page = { ...scope, cardVersion: 4, items: [row], nextCursor: null, canComment: true };
const ack = { ...scope, cardVersion: 5, comment: row, changed: true };
const props = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false,
  onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const writes = () => vi.mocked(workRequest).mock.calls.filter(([, init]) => !!init?.method);
function mock(write: () => unknown = () => ack, value: unknown = page) {
  vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? profile : init?.method ? write() : value);
}
async function review() { fireEvent.click(screen.getByRole('button', { name: 'Review Card comments' })); await screen.findByRole('button', { name: 'Add comment' }); }
async function create() { await review(); fireEvent.click(screen.getByRole('button', { name: 'Add comment' })); fireEvent.change(screen.getByRole('textbox', { name: 'New comment' }), { target: { value: row.content } }); }
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
function telemetry() { configureActivityTelemetry(true); const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch); return fetch; }
it('PRD-02 AUTH-FR-010 uses admitted timezone for review and fresh preferences for original acknowledgment recovery', async () => {
  let timezone = 'Pacific/Honolulu'; let attempts = 0;
  vi.mocked(workRequest).mockImplementation(async (path, init) => {
    if (path === '/me') return { ...profile, timezone };
    if (init?.method) { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; }
    return page;
  });
  const p = props(); const view = render(<CardCommentsControl {...p} />); await review();
  expect(screen.getByText(/You · Oct 2, 2026, 22:00/)).toBeInTheDocument();
  expect(screen.queryByText(new RegExp(row.createdAt))).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Add comment' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'New comment' }), { target: { value: row.content } });
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  await screen.findByRole('button', { name: 'Retry original comment change' });
  timezone = 'Asia/Tokyo'; view.rerender(<CardCommentsControl {...p} version={5} />);
  const retry = screen.getByRole('button', { name: 'Retry original comment change' });
  await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByText('Comment added.');
  expect(screen.getByText(/You · Oct 3, 2026, 17:00/)).toBeInTheDocument();
  expect(writes()).toHaveLength(2); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
  expect(ack.comment.createdAt).toBe('2026-10-03T08:00:00.123456Z');
});
it('retains the original comment intent when admission becomes unavailable during retry preflight', async () => {
  let attempts = 0;
  mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; });
  const p = props(); const view = render(<CardCommentsControl {...p} />); await create();
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  const retry = await screen.findByRole('button', { name: 'Retry original comment change' });
  await waitFor(() => expect(retry).toBeEnabled());
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(value => { resolve = value; }));
  fireEvent.click(retry);
  await waitFor(() => expect(resolve).toBeTypeOf('function'));
  view.rerender(<CardCommentsControl {...p} unavailable />);
  await act(async () => resolve(profile));
  await waitFor(() => expect(p.onBusyChange).toHaveBeenLastCalledWith(false));
  expect(writes()).toHaveLength(1);
  expect(screen.getByText('The comment change is unconfirmed. Retry the original request to recover its acknowledgment.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original comment change' })).toBeNull();
  expect(screen.queryByText('This comment change is unavailable. Load the latest Card before starting another change.')).toBeNull();
  view.rerender(<CardCommentsControl {...p} version={5} />);
  const recoveredRetry = await screen.findByRole('button', { name: 'Retry original comment change' });
  await waitFor(() => expect(recoveredRetry).toBeEnabled()); fireEvent.click(recoveredRetry);
  await screen.findByText('Comment added.'); expect(writes()).toHaveLength(2);
  expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
});
it('requires explicit group confirmations and preserves both scopes on original lost-reply recovery', async () => {
  const reports = telemetry();
  let attempts = 0; const content = '@card @board';
  mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return { ...ack, comment: { ...row, content } }; });
  render(<CardCommentsControl {...props()} canAdminister />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Add comment' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'New comment' }), { target: { value: content } });
  const cardConsent = screen.getByRole('checkbox', { name: 'Notify current teammates assigned to this Card (@card)' });
  const boardConsent = screen.getByRole('checkbox', { name: 'Notify all current board participants (@board)' });
  expect(cardConsent).not.toBeChecked(); expect(boardConsent).not.toBeChecked();
  fireEvent.click(cardConsent); fireEvent.click(boardConsent); fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original comment change' }));
  await screen.findByText('Comment added.');
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ content, cardVersion: 4, massMentionConfirmation: { card: true, board: true } });
  expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body); expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
  await flushActivityTelemetry(); const observations = JSON.parse(reports.mock.calls[0][1].body).events;
  expect(observations).toEqual(expect.arrayContaining([
    { action: 'comment_disclosure', kind: 'open', count: 1 }, { action: 'card_group_confirmation', kind: 'use', count: 1 },
    { action: 'board_group_confirmation', kind: 'use', count: 1 }, { action: 'comment_create', kind: 'retry', count: 1 },
    { action: 'comment_create', kind: 'failure', count: 1, durationMs: expect.any(Number) },
    { action: 'comment_create', kind: 'success', count: 1, durationMs: expect.any(Number) },
  ]));
  const body = reports.mock.calls[0][1].body; expect(body).not.toContain('@card'); expect(body).not.toContain(scope.cardId);
  expect(body).not.toContain((writes()[0][1]!.headers as Record<string, string>)['Idempotency-Key']);
});
it('keeps unconfirmed groups literal, resets consent after text changes, and refuses Board consent without administration', async () => {
  mock(() => ({ ...ack, comment: { ...row, content: '@card @board edited' } }));
  render(<CardCommentsControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Add comment' }));
  const editor = screen.getByRole('textbox', { name: 'New comment' });
  fireEvent.change(editor, { target: { value: 'x@board https://example.test/@card' } });
  expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  fireEvent.change(editor, { target: { value: '@card @board' } });
  expect(screen.getByRole('checkbox', { name: 'Notify all current board participants (@board)' })).toBeDisabled();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Notify current teammates assigned to this Card (@card)' }));
  fireEvent.change(editor, { target: { value: '@card @board edited' } });
  expect(screen.getByRole('checkbox', { name: 'Notify current teammates assigned to this Card (@card)' })).not.toBeChecked();
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' })); await screen.findByText('Comment added.');
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ content: '@card @board edited', cardVersion: 4 });
});
it('inserts an explicitly reviewed teammate with immutable account/handle revision and preserves the same selection on lost-reply recovery', async () => {
  const reports = telemetry();
  const teammate = { userId: id(9), handle: 'current_teammate', displayName: 'Current teammate', handleVersion: 3 };
  let attempts = 0;
  vi.mocked(workRequest).mockImplementation(async (path, init) => {
    if (path === '/me') return profile;
    if (init?.method) { if (++attempts === 1) throw new WorkRequestError(503, null); return { ...ack, comment: { ...row, content: '@current_teammate' } }; }
    return path.includes('/mention-options') ? { ...scope, cardVersion: 4, prefix: 'current_', items: [teammate], nextCursor: null } : page;
  });
  render(<CardCommentsControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Add comment' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'Teammate username prefix' }), { target: { value: 'current_' } });
  fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  const option = await screen.findByRole('button', { name: 'Mention Current teammate (@current_teammate)' });
  fireEvent.click(option); expect(screen.getByRole('textbox', { name: 'New comment' })).toHaveValue('@current_teammate');
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original comment change' }));
  await screen.findByText('Comment added.'); expect(writes()).toHaveLength(2);
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ content: '@current_teammate', cardVersion: 4,
    mentionSelections: [{ userId: teammate.userId, handle: teammate.handle, handleVersion: 3 }] });
  expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body); expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
  await flushActivityTelemetry(); const body = reports.mock.calls[0][1].body;
  expect(JSON.parse(body).events).toEqual(expect.arrayContaining([
    { action: 'mention_read', kind: 'use', count: 1 }, { action: 'mention_selection', kind: 'use', count: 1 },
    { action: 'mention_read', kind: 'success', count: 1, durationMs: expect.any(Number) },
  ]));
  expect(body).not.toContain(teammate.userId); expect(body).not.toContain(teammate.handle); expect(body).not.toContain(teammate.displayName);
});
it('retires pending teammate metadata when the Card revision changes before lookup admission', async () => {
  let finish!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : path.includes('/mention-options')
    ? new Promise(resolve => { finish = resolve; }) : page);
  const p = props(); const view = render(<CardCommentsControl {...p} />); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Add comment' })); fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  await waitFor(() => expect(finish).toBeDefined());
  view.rerender(<CardCommentsControl {...p} version={5} />);
  finish({ ...scope, cardVersion: 4, prefix: '', items: [{ userId: id(9), handle: 'former_teammate', displayName: 'Former choice', handleVersion: 2 }], nextCursor: null });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Save comment' })).toBeDisabled());
  expect(screen.queryByText(/Former choice/)).not.toBeInTheDocument(); expect(writes()).toHaveLength(0);
});
it('renders literal plaintext safely and submits a normalized scoped create with a fresh retry key', async () => {
  mock(); const p = props(); const view = render(<CardCommentsControl {...p} />); expect(workRequest).not.toHaveBeenCalled();
  await review(); expect(screen.getByText(row.content)).toBeInTheDocument(); expect(view.container.querySelector('script')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Add comment' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'New comment' }), { target: { value: ` \t${row.content}\r\n ` } });
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true));
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' })); await screen.findByText('Comment added.');
  expect(writes()).toHaveLength(1); expect(writes()[0][0]).toBe(`/cards/${scope.cardId}/comments`);
  expect(writes()[0][1]!.method).toBe('POST'); expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ content: row.content, cardVersion: 4 });
  expect((writes()[0][1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false)); expect(p.onRefresh).toHaveBeenCalledOnce();
});
it('requires deletion confirmation and sends both captured revisions before accepting a redacted tombstone', async () => {
  mock(() => ({ ...ack, comment: { ...row, content: null, version: 2, deletedAt: row.updatedAt, deletedBy: row.authorId } }));
  render(<CardCommentsControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Remove comment body' }));
  const save = screen.getByRole('button', { name: 'Confirm comment removal' }); expect(save).toBeDisabled(); expect(writes()).toHaveLength(0);
  fireEvent.click(screen.getByRole('checkbox', { name: 'I confirm removal of my comment body' })); fireEvent.click(save);
  await screen.findByText('Comment body removed.'); expect(writes()[0][1]!.method).toBe('DELETE');
  expect(writes()[0][0]).toBe(`/cards/${scope.cardId}/comments/${row.id}`);
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ cardVersion: 4, version: 1, confirmed: true });
});
it('accepts an equal normalized author edit as a no-op without inventing new revisions', async () => {
  mock(() => ({ ...ack, cardVersion: 4, changed: false })); render(<CardCommentsControl {...props()} />); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Edit comment' })); fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  await screen.findByText('Comment saved.'); expect(writes()[0][1]!.method).toBe('PATCH');
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ content: row.content, cardVersion: 4, version: 1 });
});
it('preserves the original key/body after a lost reply and newer snapshots while respecting another focus owner', async () => {
  let attempts = 0; mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; });
  const p = props(); const view = render(<Dialog open><CardCommentsControl {...p} /><Button>Another control</Button></Dialog>);
  await create(); const save = screen.getByRole('button', { name: 'Save comment' }); save.focus(); fireEvent.click(save);
  const retry = await screen.findByRole('button', { name: 'Retry original comment change' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.getByRole('textbox')).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Discard comment review and load latest' })).not.toBeInTheDocument();
  view.rerender(<Dialog open><CardCommentsControl {...p} version={9} unavailable /><Button>Another control</Button></Dialog>);
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument(); screen.getByRole('button', { name: 'Another control' }).focus();
  view.rerender(<Dialog open><CardCommentsControl {...p} version={9} /><Button>Another control</Button></Dialog>);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original comment change' })).toBeEnabled());
  expect(screen.getByRole('button', { name: 'Another control' })).toHaveFocus(); fireEvent.click(screen.getByRole('button', { name: 'Retry original comment change' }));
  await screen.findByText('Comment added.'); expect(writes()).toHaveLength(2); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
});
it.each([400, 401, 403, 404, 409, 429])('retires protected review on definite %s refusal and requires explicit discard before new work', async status => {
  const reports = telemetry();
  mock(() => { throw new WorkRequestError(status, null); }); const p = props(); render(<CardCommentsControl {...p} />); await create();
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  await screen.findByText(status === 429 ? /Group mentions are limited to three deliveries/ : /This comment change is unavailable/);
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Retry original comment change' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Review Card comments' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Discard comment review and load latest' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review Card comments' })).toBeEnabled());
  expect(writes()).toHaveLength(1);
  await flushActivityTelemetry(); const observations = JSON.parse(reports.mock.calls[0][1].body).events;
  expect(observations).toEqual(expect.arrayContaining([{ action: 'comment_create', kind: 'failure', count: 1, durationMs: expect.any(Number) }]));
  expect(observations.filter((item: { kind: string }) => item.kind === 'conflict')).toEqual(status === 409 ? [{ action: 'comment_create', kind: 'conflict', count: 1 }] : []);
});
it('does not offer foreign-author mutations or disclose old rows under unavailable/newer context', async () => {
  mock(() => ack, { ...page, items: [{ ...row, authorId: id(99) }] }); const p = props(); const view = render(<CardCommentsControl {...p} />); await review();
  expect(screen.queryByRole('button', { name: 'Edit comment' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Remove comment body' })).not.toBeInTheDocument();
  view.rerender(<CardCommentsControl {...p} version={5} />); expect(screen.queryByText(row.content)).not.toBeInTheDocument();
  view.rerender(<CardCommentsControl {...p} unavailable />); expect(screen.queryByText(row.content)).not.toBeInTheDocument(); expect(writes()).toHaveLength(0);
});
it('rejects changed account before a write and treats malformed acknowledgment as uncertain original recovery', async () => {
  mock(() => ({ ...ack, privateBody: 'hidden' })); render(<CardCommentsControl {...props()} />); await create();
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' })); await screen.findByRole('button', { name: 'Retry original comment change' });
  expect(screen.queryByText('Comment added.')).not.toBeInTheDocument();
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? { ...profile, id: id(99) } : ack);
  fireEvent.click(screen.getByRole('button', { name: 'Retry original comment change' })); await screen.findByText(/This comment change is unavailable/);
  expect(writes()).toHaveLength(1);
});
it('explains read-only empty state and blocks new commands when the Card changes during a dirty review', async () => {
  mock(() => ack, { ...page, items: [], canComment: false }); const p = props(); const view = render(<CardCommentsControl {...p} />); await review();
  expect(screen.getByRole('button', { name: 'Add comment' })).toBeDisabled(); expect(screen.getByText('No comments on this page.')).toBeInTheDocument();
  view.unmount(); mock(); const next = render(<CardCommentsControl {...p} />); await create(); next.rerender(<CardCommentsControl {...p} version={5} />);
  expect(screen.getByRole('button', { name: 'Save comment' })).toBeDisabled(); expect(screen.getByRole('textbox')).toBeDisabled(); expect(writes()).toHaveLength(0);
});
it('automatically rereads an opened clean view after Card invalidation and reconnect without stealing focus', async () => {
  const reports = telemetry();
  let current: unknown = page; const p = props();
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : current);
  const view = render(<><CardCommentsControl {...p} reconnectSequence={0} /><Button>Another control</Button></>); await review();
  screen.getByRole('button', { name: 'Another control' }).focus();
  current = { ...page, cardVersion: 5, items: [{ ...row, content: 'Updated by another client', version: 2,
    updatedAt: '2026-10-03T08:01:00.123456Z', editedAt: '2026-10-03T08:01:00.123456Z' }] };
  view.rerender(<><CardCommentsControl {...p} version={5} reconnectSequence={0} /><Button>Another control</Button></>);
  await screen.findByText('Updated by another client'); expect(screen.queryByText(row.content)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Another control' })).toHaveFocus();
  const reads = () => vi.mocked(workRequest).mock.calls.filter(([path]) => path.includes('/comments')).length;
  expect(reads()).toBe(2);
  view.rerender(<><CardCommentsControl {...p} version={5} reconnectSequence={1} /><Button>Another control</Button></>);
  await waitFor(() => expect(reads()).toBe(3)); await screen.findByText('Updated by another client');
  expect(screen.getByRole('button', { name: 'Another control' })).toHaveFocus(); expect(writes()).toHaveLength(0);
  await flushActivityTelemetry(); expect(JSON.parse(reports.mock.calls[0][1].body).events).toEqual(expect.arrayContaining([
    { action: 'comment_read', kind: 'reconnect', count: 1 }, { action: 'comment_read', kind: 'use', count: 3 },
  ]));
});
