import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BoardDateProvider, CardDueBadge, cardDueDescriptionId } from './BoardDateBadges';
import { nextCardDateWake } from './cardDates';
import { workRequest, WorkRequestError, type BoardSnapshot, type WorkCard } from '../../api/workManagement';
import { watchIdentity } from '../auth/identityLive';
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const profile = { id: '22222222-2222-2222-2222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const card: WorkCard = { id: 'card', title: 'Card', description: null, rank: 'rank', version: 1, startAt: null,
  dueAt: '2026-10-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: false, dueComplete: false };
const snapshot = (cards = [card], timezone: string | null = null): BoardSnapshot => ({
  board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active', dateTimezoneOverride: timezone },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: true },
  lists: [{ list: { id: 'list', name: 'List', rank: 'rank', lifecycleState: 'active' }, cards }],
});
const contents = (cards: WorkCard[]) => cards.map(value => <a key={value.id} href={`/cards/${value.id}`} aria-label={value.title}
  aria-describedby={value.dueAt ? cardDueDescriptionId(value.id) : undefined}><CardDueBadge card={value} /></a>);
beforeEach(() => { vi.mocked(workRequest).mockReset(); vi.mocked(watchIdentity).mockClear(); vi.useFakeTimers({ toFake: ['Date'] }); vi.setSystemTime(new Date('2026-10-03T01:00:00Z')); });
afterEach(() => { vi.useRealTimers(); });
it('retains delivered viewer preferences under the Board override and uses them after clearing it', async () => {
  vi.setSystemTime(new Date('2026-10-02T23:00:00Z')); vi.mocked(workRequest).mockResolvedValue(profile);
  const p = { unavailable: false, onRevalidate: vi.fn(), children: contents([card]) };
  const view = render(<BoardDateProvider {...p} snapshot={snapshot([card], 'UTC')} />);
  await screen.findByText('Due soon'); await waitFor(() => expect(watchIdentity).toHaveBeenCalledOnce());
  vi.mocked(workRequest).mockResolvedValue({ ...profile, version: 2, timezone: 'Pacific/Honolulu' });
  await act(async () => vi.mocked(watchIdentity).mock.calls[0][0].invalidate());
  await waitFor(() => expect(workRequest).toHaveBeenCalledTimes(2)); expect(screen.getByText('Due soon')).toBeVisible();
  view.rerender(<BoardDateProvider {...p} snapshot={snapshot()} />);
  expect(screen.getByText('Due today')).toBeVisible(); expect(workRequest).toHaveBeenCalledTimes(2);
  expect(p.onRevalidate).not.toHaveBeenCalled(); expect(card.dueAt).toBe('2026-10-03T08:00:00Z');
});
it('recovers delivered viewer preferences without changing Card or Board policy and coalesces pending delivery', async () => {
  vi.setSystemTime(new Date('2026-10-02T23:00:00Z')); let reads = 0, admit!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementation(async () => ++reads === 2 ? new Promise(resolve => { admit = resolve; })
    : reads >= 3 ? { ...profile, version: 3, timezone: 'Pacific/Honolulu' } : profile);
  const refresh = vi.fn();
  render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={refresh}>{contents([card])}</BoardDateProvider>);
  await screen.findByText('Due soon'); await waitFor(() => expect(watchIdentity).toHaveBeenCalledOnce());
  const invalidate = vi.mocked(watchIdentity).mock.calls[0][0].invalidate;
  act(() => invalidate()); await waitFor(() => expect(admit).toBeTypeOf('function')); act(() => invalidate());
  await act(async () => admit({ ...profile, version: 2 }));
  await screen.findByText('Due today'); expect(reads).toBe(3); expect(refresh).not.toHaveBeenCalled();
  expect(card.dueAt).toBe('2026-10-03T08:00:00Z'); expect(watchIdentity).toHaveBeenCalledOnce();
});
it('recovers viewer admission on online and disposes its identity delivery on unmount', async () => {
  vi.mocked(workRequest).mockRejectedValueOnce(new Error('private offline diagnostic')).mockResolvedValue(profile);
  const view = render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={vi.fn()}>{contents([card])}</BoardDateProvider>);
  await screen.findByRole('alert'); act(() => window.dispatchEvent(new Event('online')));
  await screen.findByText('Due today'); await waitFor(() => expect(watchIdentity).toHaveBeenCalledOnce());
  view.unmount(); expect(vi.mocked(watchIdentity).mock.results[0].value).toHaveBeenCalledOnce();
  act(() => window.dispatchEvent(new Event('online'))); expect(workRequest).toHaveBeenCalledTimes(2);
});
it.each([401, 403, 404])('retires queued/delivered and fallback date reads after denial (%s)', async status => {
  vi.useFakeTimers(); vi.mocked(workRequest).mockResolvedValueOnce(profile).mockRejectedValue(new WorkRequestError(status, null));
  await act(async () => { render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={vi.fn()}>{contents([card])}</BoardDateProvider>); await vi.advanceTimersByTimeAsync(0); });
  const invalidate = vi.mocked(watchIdentity).mock.calls[0][0].invalidate;
  await act(async () => { invalidate(); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.getByRole('link')).toHaveAccessibleDescription('Due status unavailable.');
  await act(async () => {
    invalidate(); window.dispatchEvent(new Event('online')); window.dispatchEvent(new Event('focus'));
    document.dispatchEvent(new Event('visibilitychange')); await vi.advanceTimersByTimeAsync(60_000);
  });
  expect(workRequest).toHaveBeenCalledTimes(2);
  expect(vi.mocked(watchIdentity).mock.results[0].value).toHaveBeenCalledOnce();
  vi.mocked(workRequest).mockResolvedValue(profile);
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Check date display' })); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(workRequest).toHaveBeenCalledTimes(3);
  await act(async () => { await vi.advanceTimersByTimeAsync(30_000); }); expect(workRequest).toHaveBeenCalledTimes(4);
});
it('shares one viewer read across dated Cards and adds a textual accessible description without changing link names', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  const cards = [card, { ...card, id: 'second', title: 'Second', dueComplete: true }];
  render(<BoardDateProvider snapshot={snapshot(cards)} unavailable={false} onRevalidate={vi.fn()}>{contents(cards)}</BoardDateProvider>);
  await waitFor(() => expect(screen.getByRole('link', { name: 'Card' })).toHaveAccessibleDescription('Due today'));
  expect(screen.getByRole('link', { name: 'Second' })).toHaveAccessibleDescription('Complete');
  expect(workRequest).toHaveBeenCalledTimes(1); expect(workRequest).toHaveBeenCalledWith('/me', expect.anything());
  expect(document.getElementById(cardDueDescriptionId(card.id))?.querySelector('svg')).not.toBeNull();
});
it('uses Board policy for calendar status and clearing policy restores the account timezone without another account read', async () => {
  vi.setSystemTime(new Date('2026-10-02T23:00:00Z'));
  vi.mocked(workRequest).mockResolvedValue(profile);
  const props = { unavailable: false, onRevalidate: vi.fn(), children: contents([card]) };
  const { rerender } = render(<BoardDateProvider {...props} snapshot={snapshot([card], 'Pacific/Honolulu')} />);
  await screen.findByText('Due today');
  rerender(<BoardDateProvider {...props} snapshot={snapshot()} />);
  expect(screen.getByText('Due soon')).toBeInTheDocument(); expect(workRequest).toHaveBeenCalledTimes(1);
});
it('does not read the viewer on a Board without due dates', () => {
  const cards = [{ ...card, dueAt: null, dueTimezone: null }];
  render(<BoardDateProvider snapshot={snapshot(cards)} unavailable={false} onRevalidate={vi.fn()}>{contents(cards)}</BoardDateProvider>);
  expect(workRequest).not.toHaveBeenCalled(); expect(screen.queryByRole('status')).not.toBeInTheDocument();
});
it('hides cached status immediately during Board revalidation and rechecks the viewer afterwards', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  const props = { snapshot: snapshot(), onRevalidate: vi.fn(), children: contents([card]) };
  const { rerender } = render(<BoardDateProvider {...props} unavailable={false} />); await screen.findByText('Due today');
  rerender(<BoardDateProvider {...props} unavailable />);
  expect(screen.queryByText('Due today')).not.toBeInTheDocument(); expect(screen.getByRole('link')).toHaveAccessibleDescription('Due status unavailable.');
  rerender(<BoardDateProvider {...props} unavailable={false} />); await screen.findByText('Due today'); expect(workRequest).toHaveBeenCalledTimes(2);
});
it.each([new WorkRequestError(401, null), new WorkRequestError(403, null), new Error('private diagnostic'), { ...profile, status: 'DELETING' }])('withholds statuses for failed or invalid viewer admission (%s)', async failure => {
  if (failure instanceof Error) vi.mocked(workRequest).mockRejectedValue(failure); else vi.mocked(workRequest).mockResolvedValue(failure);
  render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={vi.fn()}>{contents([card])}</BoardDateProvider>);
  await screen.findByRole('alert'); expect(screen.getByRole('link')).toHaveAccessibleDescription('Due status unavailable.');
  expect(screen.queryByText('Due today')).not.toBeInTheDocument(); expect(screen.queryByText(/private diagnostic/)).not.toBeInTheDocument();
});
it('withholds statuses under an invalid Board policy', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  render(<BoardDateProvider snapshot={snapshot([card], 'Unknown/Place')} unavailable={false} onRevalidate={vi.fn()}>{contents([card])}</BoardDateProvider>);
  await screen.findByRole('alert'); expect(screen.getByRole('link')).toHaveAccessibleDescription('Due status unavailable.');
});
it('withholds malformed canonical Card dates instead of inventing a status', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile); const cards = [{ ...card, dueTimezone: null }];
  render(<BoardDateProvider snapshot={snapshot(cards)} unavailable={false} onRevalidate={vi.fn()}>{contents(cards)}</BoardDateProvider>);
  await screen.findByText('Due status unavailable'); expect(screen.queryByText('Due today')).not.toBeInTheDocument();
});
it('account switches require Board revalidation and cannot reveal statuses on another focus event', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile); const refresh = vi.fn();
  render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={refresh}>{contents([card])}</BoardDateProvider>);
  await screen.findByText('Due today');
  vi.mocked(workRequest).mockResolvedValue({ ...profile, id: '33333333-3333-3333-3333-333333333333' });
  act(() => window.dispatchEvent(new Event('focus'))); await screen.findByRole('alert'); expect(refresh).toHaveBeenCalledOnce();
  act(() => window.dispatchEvent(new Event('focus'))); expect(screen.queryByText('Due today')).not.toBeInTheDocument(); expect(workRequest).toHaveBeenCalledTimes(2);
});
it('fences an outstanding viewer response after the Board becomes unavailable', async () => {
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementation(() => new Promise(done => { resolve = done; }));
  const props = { snapshot: snapshot(), onRevalidate: vi.fn(), children: contents([card]) };
  const { rerender } = render(<BoardDateProvider {...props} unavailable={false} />);
  await waitFor(() => expect(workRequest).toHaveBeenCalledOnce()); const signal = vi.mocked(workRequest).mock.calls[0][1]!.signal!;
  rerender(<BoardDateProvider {...props} unavailable />); expect(signal.aborted).toBe(true);
  await act(async () => resolve(profile)); expect(screen.queryByText('Due today')).not.toBeInTheDocument();
});
it('bounds stalled viewer reads and ignores their late completion', async () => {
  vi.useFakeTimers(); let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementation(() => new Promise(done => { resolve = done; }));
  render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={vi.fn()}>{contents([card])}</BoardDateProvider>);
  await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
  expect(screen.getByRole('alert')).toHaveTextContent('Due statuses are unavailable');
  await act(async () => resolve(profile)); expect(screen.queryByText('Due today')).not.toBeInTheDocument();
});
it('refreshes a changed timezone for the same account on focus', async () => {
  vi.setSystemTime(new Date('2026-10-02T23:00:00Z')); vi.mocked(workRequest).mockResolvedValue(profile);
  const refresh = vi.fn();
  render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={refresh}>{contents([card])}</BoardDateProvider>);
  await screen.findByText('Due soon'); vi.mocked(workRequest).mockResolvedValue({ ...profile, version: 2, timezone: 'Pacific/Honolulu' });
  act(() => window.dispatchEvent(new Event('focus'))); await screen.findByText('Due today'); expect(refresh).not.toHaveBeenCalled();
});
it('changes an idle Board from due today to overdue just after the due instant, retaining completed status', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-03T07:59:59Z'));
  vi.mocked(workRequest).mockResolvedValue(profile);
  const cards = [card, { ...card, id: 'second', title: 'Second', dueComplete: true }];
  render(<BoardDateProvider snapshot={snapshot(cards)} unavailable={false} onRevalidate={vi.fn()}>{contents(cards)}</BoardDateProvider>);
  await act(async () => { await vi.advanceTimersByTimeAsync(0); }); expect(screen.getByText('Due today')).toBeInTheDocument();
  await act(async () => { await vi.advanceTimersByTimeAsync(1001); });
  expect(screen.getByRole('link', { name: 'Card' })).toHaveAccessibleDescription('Overdue');
  expect(screen.getByRole('link', { name: 'Second' })).toHaveAccessibleDescription('Complete'); expect(workRequest).toHaveBeenCalledOnce();
});
it('changes the calendar status at local midnight without another viewer read', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-02T23:59:59Z')); vi.mocked(workRequest).mockResolvedValue(profile);
  render(<BoardDateProvider snapshot={snapshot()} unavailable={false} onRevalidate={vi.fn()}>{contents([card])}</BoardDateProvider>);
  await act(async () => { await vi.advanceTimersByTimeAsync(0); }); expect(screen.getByText('Due soon')).toBeInTheDocument();
  await act(async () => { await vi.advanceTimersByTimeAsync(1000); }); expect(screen.getByText('Due today')).toBeInTheDocument(); expect(workRequest).toHaveBeenCalledOnce();
});
it('schedules the 24-hour boundary, local midnight and a bounded heartbeat', () => {
  expect(nextCardDateWake([card], 'UTC', Date.parse('2026-10-02T07:59:59Z'))).toBe(1000);
  expect(nextCardDateWake([card], 'Pacific/Honolulu', Date.parse('2026-10-03T09:59:59Z'))).toBe(1000);
  expect(nextCardDateWake([], 'UTC', Date.parse('2026-10-03T01:00:00Z'))).toBe(30_000);
  expect(nextCardDateWake([{ ...card, dueAt: '2026-10-03T08:00:00.0000001Z' }], 'UTC', Date.parse('2026-10-02T08:00:00Z'))).toBe(1);
});
