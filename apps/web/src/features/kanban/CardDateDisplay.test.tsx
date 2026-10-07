import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardDateDisplay } from './CardDateDisplay';
import { workRequest, WorkRequestError, type WorkCard } from '../../api/workManagement';
import { watchIdentity } from '../auth/identityLive';
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const profile = { id: '22222222-2222-2222-2222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'Pacific/Honolulu' };
const card: WorkCard = { id: 'card', title: 'Card', description: null, rank: 'rank', version: 1,
  startAt: null, dueAt: '2026-10-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: false, dueComplete: true };
const props = () => ({ card, organizationId: 'org', boardId: 'board', unavailable: false, onRefresh: vi.fn() });
beforeEach(() => { vi.mocked(workRequest).mockReset(); vi.mocked(watchIdentity).mockClear(); });
afterEach(() => { vi.useRealTimers(); });
it('retains live viewer preferences behind a Board policy and uses them when the policy is cleared', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  const p = props(); const view = render(<CardDateDisplay {...p} boardTimezone="UTC" />);
  await screen.findByRole('region', { name: 'Card dates' }); await waitFor(() => expect(watchIdentity).toHaveBeenCalledOnce());
  vi.mocked(workRequest).mockResolvedValue({ ...profile, version: 2, timezone: 'Asia/Tokyo' });
  await act(async () => vi.mocked(watchIdentity).mock.calls[0][0].invalidate());
  await waitFor(() => expect(workRequest).toHaveBeenCalledTimes(2));
  expect(screen.getByRole('region', { name: 'Card dates' })).toHaveTextContent('Viewing timezone: UTC.');
  view.rerender(<CardDateDisplay {...p} boardTimezone={null} />);
  expect(screen.getByRole('region', { name: 'Card dates' })).toHaveTextContent('Viewing timezone: Asia/Tokyo.');
  expect(workRequest).toHaveBeenCalledTimes(2); expect(card.dueAt).toBe('2026-10-03T08:00:00Z');
});
it('automatically recovers delivered preferences and coalesces another delivery during a pending profile read', async () => {
  let reads = 0, admit!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementation(async () => ++reads === 2
    ? new Promise(resolve => { admit = resolve; }) : reads >= 3 ? { ...profile, version: 3, timezone: 'Asia/Tokyo' } : profile);
  render(<CardDateDisplay {...props()} />); await screen.findByRole('region', { name: 'Card dates' });
  await waitFor(() => expect(watchIdentity).toHaveBeenCalledOnce());
  const invalidate = vi.mocked(watchIdentity).mock.calls[0][0].invalidate;
  act(() => invalidate()); await waitFor(() => expect(admit).toBeTypeOf('function'));
  act(() => invalidate());
  await act(async () => admit({ ...profile, version: 2, timezone: 'UTC' }));
  await waitFor(() => expect(screen.getByRole('region', { name: 'Card dates' })).toHaveTextContent('Viewing timezone: Asia/Tokyo.'));
  expect(reads).toBe(3); expect(watchIdentity).toHaveBeenCalledOnce();
  expect(card.dueAt).toBe('2026-10-03T08:00:00Z');
});
it('recovers failed date admission on online without a manual refresh and retires listeners on unmount', async () => {
  vi.mocked(workRequest).mockRejectedValueOnce(new Error('private offline diagnostic')).mockResolvedValue({ ...profile, timezone: 'UTC' });
  const view = render(<CardDateDisplay {...props()} />); await screen.findByRole('alert');
  act(() => window.dispatchEvent(new Event('online')));
  await screen.findByRole('region', { name: 'Card dates' });
  expect(screen.queryByText(/private offline/)).not.toBeInTheDocument();
  await waitFor(() => expect(watchIdentity).toHaveBeenCalledOnce());
  const cleanup = vi.mocked(watchIdentity).mock.results[0].value;
  view.unmount(); expect(cleanup).toHaveBeenCalledOnce();
  act(() => window.dispatchEvent(new Event('online'))); expect(workRequest).toHaveBeenCalledTimes(2);
});
it.each([401, 403, 404])('retires delivered and fallback reads after denied date admission (%s)', async status => {
  vi.useFakeTimers(); vi.mocked(workRequest).mockResolvedValueOnce(profile).mockRejectedValue(new WorkRequestError(status, null));
  await act(async () => { render(<CardDateDisplay {...props()} />); await vi.advanceTimersByTimeAsync(0); });
  const invalidate = vi.mocked(watchIdentity).mock.calls[0][0].invalidate;
  await act(async () => { invalidate(); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.queryByRole('region', { name: 'Card dates' })).not.toBeInTheDocument();
  await act(async () => {
    invalidate(); window.dispatchEvent(new Event('online')); window.dispatchEvent(new Event('focus'));
    document.dispatchEvent(new Event('visibilitychange')); await vi.advanceTimersByTimeAsync(60_000);
  });
  expect(workRequest).toHaveBeenCalledTimes(2);
  expect(vi.mocked(watchIdentity).mock.results[0].value).toHaveBeenCalledOnce();
  vi.mocked(workRequest).mockResolvedValue(profile);
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Refresh dates' })); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.getByRole('region', { name: 'Card dates' })).toBeVisible(); expect(workRequest).toHaveBeenCalledTimes(3);
  await act(async () => { await vi.advanceTimersByTimeAsync(30_000); }); expect(workRequest).toHaveBeenCalledTimes(4);
});
it('updates an idle Card to overdue just after its due instant without another profile read', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-03T07:59:59Z'));
  try {
    vi.mocked(workRequest).mockResolvedValue({ ...profile, timezone: 'UTC' });
    render(<CardDateDisplay {...props()} card={{ ...card, dueComplete: false }} />);
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(screen.getByRole('region', { name: 'Card dates' })).toHaveTextContent('Due today');
    await act(async () => { await vi.advanceTimersByTimeAsync(1001); });
    expect(screen.getByRole('region', { name: 'Card dates' })).toHaveTextContent('Overdue'); expect(workRequest).toHaveBeenCalledOnce();
  } finally { vi.useRealTimers(); }
});
it('applies the current Board timezone policy and clearing it restores the viewing account timezone', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  const p = props(); const { rerender } = render(<CardDateDisplay {...p} boardTimezone="UTC" />);
  const region = await screen.findByRole('region', { name: 'Card dates' });
  expect(region).toHaveTextContent('Due Oct 3, 2026'); expect(region).toHaveTextContent('Board timezone policy.');
  rerender(<CardDateDisplay {...p} boardTimezone={null} />);
  expect(region).toHaveTextContent('Due Oct 2, 2026'); expect(region).not.toHaveTextContent('Board timezone policy.');
  expect(card.dueAt).toBe('2026-10-03T08:00:00Z');
});
it('withholds dates when a Board policy contains an invalid timezone', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  render(<CardDateDisplay {...props()} boardTimezone="Unknown/Place" />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Dates are unavailable');
  expect(screen.queryByRole('region', { name: 'Card dates' })).not.toBeInTheDocument();
});
it('shows configured timezone, date-only text and a textual completion status with an icon', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  render(<CardDateDisplay {...props()} />);
  const region = await screen.findByRole('region', { name: 'Card dates' });
  expect(region).toHaveTextContent('Due Oct 2, 2026'); expect(region).toHaveTextContent('Complete');
  expect(region).toHaveTextContent('Viewing timezone: Pacific/Honolulu. Date context: UTC.');
  expect(region).not.toHaveTextContent('10:00'); expect(region.querySelector('svg')).not.toBeNull();
});
it('adds no account reads to Cards without dates and removes dates when the scope is unavailable', async () => {
  const p = props(); vi.mocked(workRequest).mockResolvedValue(profile);
  const { rerender } = render(<CardDateDisplay {...p} card={{ ...card, dueAt: null }} />);
  expect(workRequest).not.toHaveBeenCalled();
  rerender(<CardDateDisplay {...p} />); await screen.findByRole('region', { name: 'Card dates' });
  rerender(<CardDateDisplay {...p} unavailable />);
  expect(screen.queryByRole('region', { name: 'Card dates' })).not.toBeInTheDocument();
});
it('does not render raw errors, invalid date context or invalid account state', async () => {
  vi.mocked(workRequest).mockRejectedValue(new Error('provider details must remain private'));
  render(<CardDateDisplay {...props()} />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Dates are unavailable');
  expect(screen.queryByText(/provider details/)).not.toBeInTheDocument();
});
it('fences a late profile read after Card identity changes', async () => {
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  vi.mocked(workRequest).mockResolvedValue({ ...profile, timezone: 'UTC' });
  const p = props(); const { rerender } = render(<CardDateDisplay {...p} />);
  await waitFor(() => expect(workRequest).toHaveBeenCalledTimes(1));
  rerender(<CardDateDisplay {...p} card={{ ...card, id: 'new-card' }} />);
  await screen.findByRole('region', { name: 'Card dates' });
  await act(async () => resolve(profile));
  expect(screen.getByRole('region', { name: 'Card dates' })).toHaveTextContent('Viewing timezone: UTC.');
  expect(screen.getByRole('region', { name: 'Card dates' })).not.toHaveTextContent('Honolulu');
});
it('account changes hide dates and request fresh Board admission', async () => {
  const p = props(); vi.mocked(workRequest).mockResolvedValue(profile);
  const view = render(<CardDateDisplay {...p} />); await screen.findByRole('region', { name: 'Card dates' });
  vi.mocked(workRequest).mockResolvedValue({ ...profile, id: '33333333-3333-3333-3333-333333333333' });
  act(() => window.dispatchEvent(new Event('focus')));
  await screen.findByRole('alert'); expect(p.onRefresh).toHaveBeenCalledOnce();
  expect(screen.queryByRole('region', { name: 'Card dates' })).not.toBeInTheDocument();
  act(() => window.dispatchEvent(new Event('focus'))); expect(workRequest).toHaveBeenCalledTimes(2);
  view.rerender(<CardDateDisplay {...p} unavailable />);
  view.rerender(<CardDateDisplay {...p} />);
  await screen.findByRole('region', { name: 'Card dates' }); expect(workRequest).toHaveBeenCalledTimes(3);
  await waitFor(() => expect(watchIdentity).toHaveBeenCalledTimes(2));
  expect(vi.mocked(watchIdentity).mock.calls[1][0].subject).toBe('33333333-3333-3333-3333-333333333333');
});
