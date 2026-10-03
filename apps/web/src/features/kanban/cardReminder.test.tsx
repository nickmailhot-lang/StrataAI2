import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardReminderControl } from './CardReminderControl';
import { parseReminderState, type ReminderState } from './cardReminder';

const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222';
const id = '33333333-3333-3333-3333-333333333333', user = '44444444-4444-4444-4444-444444444444';
const other = '55555555-5555-5555-5555-555555555555', reminderId = '66666666-6666-6666-6666-666666666666';
const dueAt = '2030-01-02T12:00:00Z';
const card = { id, title: 'Due work', description: null, rank: '500000000000000000000000000000', version: 3,
  startAt: null, dueAt, dueTimezone: 'UTC', dueHasTime: true, dueComplete: false };
const profile = { id: user, version: 1, status: 'ACTIVE', locale: 'en-CA', timezone: 'America/Vancouver', emailVerified: true };
const initial: ReminderState = { organizationId: org, boardId: board, cardId: id, userId: user, cardVersion: 3, reminder: null,
  canChange: true, changed: false, options: [
    { code: 'AT_DUE', label: 'At the due time', triggerAt: dueAt },
    { code: '5_MINUTES', label: '5 minutes before', triggerAt: '2030-01-02T11:55:00Z' },
    { code: '1_HOUR', label: '1 hour before', triggerAt: '2030-01-02T11:00:00Z' },
    { code: '1_DAY', label: '1 day before', triggerAt: '2030-01-01T12:00:00Z' },
  ] };
const scheduled: ReminderState = { ...initial, reminder: { id: reminderId, organizationId: org, cardId: id, userId: user,
  intervalCode: '1_HOUR', enabled: true, dueAt, triggerAt: '2030-01-02T11:00:00Z', status: 'SCHEDULED', generation: 1, version: 1,
  createdAt: '2026-10-02T12:00:00Z', updatedAt: '2026-10-02T12:00:00Z' } };
const cancelled: ReminderState = { ...scheduled, changed: true, reminder: { ...scheduled.reminder!, enabled: false, dueAt: null,
  triggerAt: null, status: 'CANCELLED', version: 2, generation: 2 } };
const scope = { organizationId: org, boardId: board, cardId: id, userId: user, dueAt };
const props = { organizationId: org, boardId: board, card, disabled: false, unavailable: false,
  onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); vi.useRealTimers(); });
async function open() { fireEvent.click(screen.getByRole('button', { name: 'Due reminder' }));
  await screen.findByRole('combobox', { name: 'Reminder interval' }); }
function choose() { fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Reminder interval' }));
  fireEvent.click(screen.getByRole('option', { name: '1 hour before' })); }

it.each([false, true])('retains save/recovery focus across live checks without stealing navigation (lost=%s)', async lost => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(initial))
    .mockResolvedValueOnce(reply(profile));
  if (lost) fetcher.mockRejectedValueOnce(new Error('Lost response'));
  else fetcher.mockResolvedValueOnce(reply({ ...scheduled, changed: true }));
  vi.stubGlobal('fetch', fetcher);
  const ui = (unavailable: boolean) => <><button>Other control</button><CardReminderControl {...props} unavailable={unavailable} /></>;
  const view = render(ui(false)); await open(); choose();
  fireEvent.click(screen.getByRole('button', { name: 'Save due reminder' }));
  const action = await screen.findByRole('button', { name: lost ? 'Retry reminder change' : 'Due reminder' });
  await waitFor(() => expect(action).toHaveFocus());
  view.rerender(ui(true)); expect(action).toBeDisabled(); action.blur();
  view.rerender(ui(false)); await waitFor(() => expect(action).toHaveFocus());
  screen.getByRole('button', { name: 'Other control' }).focus();
  view.rerender(ui(true)); view.rerender(ui(false));
  expect(screen.getByRole('button', { name: 'Other control' })).toHaveFocus();
  expect(fetcher).toHaveBeenCalledTimes(4);
});

it('validates scoped personal data and exact UTC interval arithmetic without losing microseconds', () => {
  expect(parseReminderState(scheduled, scope)).toEqual(scheduled);
  const due = '2030-01-02T12:00:00.123456Z'; const precise = { ...scheduled,
    options: [{ code: '1_HOUR', label: '1 hour before', triggerAt: '2030-01-02T11:00:00.123456Z' }],
    reminder: { ...scheduled.reminder!, dueAt: due, triggerAt: '2030-01-02T11:00:00.123456Z' } };
  expect(parseReminderState(precise, { ...scope, dueAt: due }).reminder?.triggerAt).toBe('2030-01-02T11:00:00.123456Z');
  expect(() => parseReminderState({ ...precise, reminder: { ...precise.reminder, triggerAt: '2030-01-02T11:00:00.123455Z' } }, { ...scope, dueAt: due })).toThrow();
});
it.each([
  { ...scheduled, userId: other }, { ...scheduled, organizationId: other }, { ...scheduled, boardId: other },
  { ...scheduled, cardId: other }, { ...scheduled, reminder: { ...scheduled.reminder, userId: other } },
  { ...scheduled, options: [...initial.options, initial.options[0]] },
  { ...scheduled, options: [initial.options[0], initial.options[0]] },
  { ...scheduled, options: [{ ...initial.options[0], label: 'Protected injected name' }] },
  { ...scheduled, reminder: { ...scheduled.reminder, triggerAt: '2030-01-02T11:00:00+04:00' } },
  { ...scheduled, reminder: { ...scheduled.reminder, version: 0 } },
  { ...scheduled, reminder: { ...scheduled.reminder, generation: 2 } },
  { ...cancelled, reminder: { ...cancelled.reminder, enabled: true } },
  { ...scheduled, reminder: { ...scheduled.reminder, status: 'SUSPENDED' } },
  { ...scheduled, reminder: { ...scheduled.reminder, updatedAt: '2026-10-01T12:00:00Z' } },
])('rejects malformed or cross-person Reminder data: %j', value => { expect(() => parseReminderState(value, scope)).toThrow(); });

it('reads fresh account/scope before saving the selected interval and restores keyboard focus after acknowledgment', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(initial))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({ ...scheduled, changed: true }));
  vi.stubGlobal('fetch', fetcher); render(<CardReminderControl {...props} />); await open(); choose();
  fireEvent.click(screen.getByRole('button', { name: 'Save due reminder' })); await screen.findByText('Due reminder saved.');
  expect(fetcher.mock.calls.map(call => call[0])).toEqual(['/me', `/cards/${id}/reminders`, '/me', `/cards/${id}/reminders`]);
  expect(fetcher.mock.calls[3][1].method).toBe('POST');
  expect(JSON.parse(fetcher.mock.calls[3][1].body)).toEqual({ intervalCode: '1_HOUR', enabled: true, cardVersion: 3, version: 0 });
  expect(fetcher.mock.calls[3][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Due reminder' })).toHaveFocus());
  expect(props.onRefresh).toHaveBeenCalledOnce();
});
it('keeps the original body/key/Card revision after response loss and live Card changes', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(initial))
    .mockResolvedValueOnce(reply(profile)).mockRejectedValueOnce(new Error('Lost response'))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({ ...scheduled, changed: true }));
  vi.stubGlobal('fetch', fetcher); const view = render(<CardReminderControl {...props} />); await open(); choose();
  fireEvent.click(screen.getByRole('button', { name: 'Save due reminder' })); await screen.findByRole('button', { name: 'Retry reminder change' });
  const first = fetcher.mock.calls[3][1];
  view.rerender(<CardReminderControl {...props} card={{ ...card, version: 4 }} />);
  expect(screen.queryByRole('combobox', { name: 'Reminder interval' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Close reminder' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry reminder change' })); await screen.findByText('Due reminder saved.');
  expect(fetcher).toHaveBeenCalledTimes(6); expect(fetcher.mock.calls[5][1].body).toBe(first.body);
  expect(fetcher.mock.calls[5][1].headers.get('Idempotency-Key')).toBe(first.headers.get('Idempotency-Key'));
  expect(props.onRecoveryChange).toHaveBeenCalledWith(true); expect(props.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it('cancels the current personal revision and validates its stable identity/generation acknowledgment', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(scheduled))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(cancelled));
  vi.stubGlobal('fetch', fetcher); render(<CardReminderControl {...props} />); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel due reminder' })); await screen.findByText('Due reminder cancelled.');
  expect(fetcher.mock.calls[3][0]).toBe(`/cards/${id}/reminders?cardVersion=3&version=1`);
  expect(fetcher.mock.calls[3][1].method).toBe('DELETE'); expect(fetcher.mock.calls[3][1].body).toBeUndefined();
});
it('reloads personal state on live invalidation even when the shared Card revision is unchanged', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(scheduled))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({ ...cancelled, changed: false }));
  vi.stubGlobal('fetch', fetcher); const view = render(<CardReminderControl {...props} />); await open();
  expect(screen.getByText('Your due reminder is scheduled.')).toBeVisible();
  view.rerender(<CardReminderControl {...props} unavailable />);
  expect(screen.queryByText('Your due reminder is scheduled.')).not.toBeInTheDocument();
  view.rerender(<CardReminderControl {...props} />); await screen.findByText('You have no active due reminder.');
  expect(fetcher).toHaveBeenCalledTimes(4); expect(screen.queryByRole('button', { name: 'Cancel due reminder' })).not.toBeInTheDocument();
});
it('does not send the retained command after the signed-in actor changes', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(initial))
    .mockResolvedValueOnce(reply(profile)).mockRejectedValueOnce(new Error('Unknown'))
    .mockResolvedValueOnce(reply({ ...profile, id: other }));
  vi.stubGlobal('fetch', fetcher); render(<CardReminderControl {...props} />); await open(); choose();
  fireEvent.click(screen.getByRole('button', { name: 'Save due reminder' })); await screen.findByRole('button', { name: 'Retry reminder change' });
  fireEvent.click(screen.getByRole('button', { name: 'Retry reminder change' }));
  await screen.findByText(/This reminder change is unavailable/);
  expect(fetcher).toHaveBeenCalledTimes(5); expect(screen.queryByRole('button', { name: 'Retry reminder change' })).not.toBeInTheDocument();
});
it('bounds a stalled personal read and offers fresh read recovery', async () => {
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {}))); render(<CardReminderControl {...props} />);
  vi.useFakeTimers(); fireEvent.click(screen.getByRole('button', { name: 'Due reminder' }));
  await act(() => vi.advanceTimersByTimeAsync(15_000));
  expect(screen.getByText(/Unable to load your reminder/)).toBeVisible();
  expect(screen.getByRole('button', { name: 'Load current reminder' })).toBeEnabled();
});
it('reloads a newer canonical Card/Reminder after conflict before another save can be reviewed', async () => {
  const next = { ...scheduled, cardVersion: 4, reminder: { ...scheduled.reminder!, version: 2, generation: 2 } };
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(initial))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({}, 409))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(next));
  vi.stubGlobal('fetch', fetcher); const view = render(<CardReminderControl {...props} />); await open(); choose();
  fireEvent.click(screen.getByRole('button', { name: 'Save due reminder' })); await screen.findByText(/This reminder change is unavailable/);
  expect(screen.queryByRole('button', { name: 'Save due reminder' })).not.toBeInTheDocument();
  view.rerender(<CardReminderControl {...props} card={{ ...card, version: 4 }} />);
  fireEvent.click(screen.getByRole('button', { name: 'Load current reminder' })); await screen.findByText('Your due reminder is scheduled.');
  expect(screen.getByRole('button', { name: 'Save due reminder' })).toBeEnabled();
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
});
it('clears a protected personal choice when a live read loses access', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(scheduled))
    .mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply({}, 403));
  vi.stubGlobal('fetch', fetcher); const view = render(<CardReminderControl {...props} />); await open();
  view.rerender(<CardReminderControl {...props} unavailable />); view.rerender(<CardReminderControl {...props} />);
  await screen.findByText('This Card or personal reminder is unavailable.');
  expect(screen.queryByRole('combobox', { name: 'Reminder interval' })).not.toBeInTheDocument();
  expect(screen.queryByText('Your due reminder is scheduled.')).not.toBeInTheDocument();
  expect(fetcher).toHaveBeenCalledTimes(4);
});
it('removes an expired interval while preserving the available due-time choice', async () => {
  const near = new Date(Date.now() + 2000).toISOString(); const due = new Date(Date.parse(near) + 300_000).toISOString();
  const state = { ...initial, options: [{ code: 'AT_DUE', label: 'At the due time', triggerAt: due },
    { code: '5_MINUTES', label: '5 minutes before', triggerAt: near }] };
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(profile)).mockResolvedValueOnce(reply(state)));
  render(<CardReminderControl {...props} card={{ ...card, dueAt: due }} />); vi.useFakeTimers();
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Due reminder' })); await vi.advanceTimersByTimeAsync(0); });
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Reminder interval' }));
  fireEvent.click(screen.getByRole('option', { name: '5 minutes before' }));
  await act(() => vi.advanceTimersByTimeAsync(3000));
  expect(screen.getByRole('button', { name: 'Save due reminder' })).toBeDisabled();
  expect(screen.getByRole('combobox', { name: 'Reminder interval' })).toHaveTextContent('Choose an interval');
});
it('aborts scope disposal and prevents a late account read from starting the private Reminder read', async () => {
  let finish!: (response: Response) => void;
  const fetcher = vi.fn((_path: string, _options?: RequestInit) => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal('fetch', fetcher);
  const view = render(<CardReminderControl {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Due reminder' }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledOnce());
  const signal = fetcher.mock.calls[0][1]!.signal as AbortSignal; view.unmount(); expect(signal.aborted).toBe(true);
  await act(async () => finish(reply(profile))); expect(fetcher).toHaveBeenCalledOnce();
});
