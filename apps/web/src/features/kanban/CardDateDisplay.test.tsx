import { act, render, screen, waitFor } from '@testing-library/react';
import { CardDateDisplay } from './CardDateDisplay';
import { workRequest, type WorkCard } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const profile = { id: '22222222-2222-2222-2222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'Pacific/Honolulu' };
const card: WorkCard = { id: 'card', title: 'Card', description: null, rank: 'rank', version: 1,
  startAt: null, dueAt: '2026-10-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: false, dueComplete: true };
const props = () => ({ card, organizationId: 'org', boardId: 'board', unavailable: false, onRefresh: vi.fn() });
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
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
  render(<CardDateDisplay {...p} />); await screen.findByRole('region', { name: 'Card dates' });
  vi.mocked(workRequest).mockResolvedValue({ ...profile, id: '33333333-3333-3333-3333-333333333333' });
  act(() => window.dispatchEvent(new Event('focus')));
  await screen.findByRole('alert'); expect(p.onRefresh).toHaveBeenCalledOnce();
  expect(screen.queryByRole('region', { name: 'Card dates' })).not.toBeInTheDocument();
});
