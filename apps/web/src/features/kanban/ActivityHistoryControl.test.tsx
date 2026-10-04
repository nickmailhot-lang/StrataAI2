import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ActivityHistoryControl } from './ActivityHistoryControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), kind: 'CARD' as const, targetId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const item = (n: number) => ({ eventId: id(n), organizationId: id(1), boardId: id(7), actorId: id(8), actorLabel: 'Historical <script>🙂',
  eventType: 'CARD_UPDATED', entityType: 'Card', entityId: id(3), version: '9223372036854775807', createdAt: '2026-10-04T07:00:00.123456Z', metadata: {}, currentBoardId: id(2) });
const page = (items = [item(100)], nextCursor: string | null = null) => ({ organizationId: id(1), kind: 'CARD', targetId: id(3), items, nextCursor });
const props = () => ({ ...scope, refreshSequence: '1', unavailable: false, onDenied: vi.fn() });
const wrap = (p: ReturnType<typeof props>) => <MemoryRouter><ActivityHistoryControl {...p} /></MemoryRouter>;
const reads = () => vi.mocked(workRequest).mock.calls.filter(([path]) => path !== '/me');
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
it('loads on demand, safely renders historical captions and replaces bounded pages with older/newer history', async () => {
  const first = page(Array.from({ length: 50 }, (_, index) => item(100 - index)), 'opaque');
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : path.includes('?after=') ? page([item(50)]) : first);
  render(wrap(props())); expect(workRequest).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await screen.findByRole('button', { name: 'Older activity' }); expect(screen.getAllByRole('listitem')).toHaveLength(50);
  expect(document.querySelector('script')).toBeNull();
  expect(screen.getAllByRole('link', { name: 'Open Card' })[0]).toHaveAttribute('href', `/app/${id(1)}/boards/${id(2)}/cards/${id(3)}`);
  fireEvent.click(screen.getByRole('button', { name: 'Older activity' }));
  await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(1));
  expect(reads().at(-1)?.[0]).toBe(`/cards/${id(3)}/activity?after=opaque`);
  fireEvent.click(screen.getByRole('button', { name: 'Newer activity' }));
  await screen.findByRole('button', { name: 'Older activity' }); expect(screen.getAllByRole('listitem')).toHaveLength(50);
});
it('purges history during access checks and refreshes on realtime/reconnect generations', async () => {
  let result = page(); vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : result);
  const p = props(); const view = render(wrap(p)); fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await screen.findByRole('listitem'); view.rerender(wrap({ ...p, unavailable: true }));
  expect(screen.queryByRole('listitem')).toBeNull(); expect(screen.getByRole('button', { name: 'Refresh Card activity' })).toBeDisabled();
  result = page([]); view.rerender(wrap({ ...p, refreshSequence: '2' }));
  await screen.findByText('No activity to review yet. Authorized changes will appear here.'); expect(reads()).toHaveLength(2);
});
it('rejects an identity change between the protected page and current profile', async () => {
  let profileReads = 0; const p = props();
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? { ...profile, id: ++profileReads === 1 ? profile.id : id(99) } : page());
  render(wrap(p)); fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await screen.findByText('Activity is unavailable. Refresh the Board to check access.');
  expect(p.onDenied).toHaveBeenCalledWith(expect.objectContaining({ status: 401 })); expect(screen.queryByRole('listitem')).toBeNull();
});
it('offers recoverable retry and newest history after an expired cursor response', async () => {
  let fail = true; vi.mocked(workRequest).mockImplementation(async path => {
    if (path === '/me') return profile; if (fail) throw new WorkRequestError(400, null); return page([]);
  });
  render(wrap(props())); fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await screen.findByText('This history page expired. Return to newest activity.'); fail = false;
  fireEvent.click(screen.getByRole('button', { name: 'Newest activity' }));
  await screen.findByText('No activity to review yet. Authorized changes will appear here.');
});
it.each([401, 403, 404])('stops protected reads after a %s denial and resumes only with fresh parent admission', async status => {
  const first = page(Array.from({ length: 50 }, (_, index) => item(100 - index)), 'opaque');
  vi.mocked(workRequest).mockImplementation(async path => {
    if (path === '/me') return profile;
    if (path.includes('?after=')) throw new WorkRequestError(status, null);
    return first;
  });
  const p = props(); const view = render(wrap(p));
  fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(50));
  fireEvent.click(screen.getByRole('button', { name: 'Older activity' }));
  await screen.findByText('Activity is unavailable. Refresh the Board to check access.');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Close activity' })).toHaveFocus());
  expect(screen.queryByRole('listitem')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Retry activity page' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Refresh Card activity' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Newest activity' })).toBeDisabled();
  expect(p.onDenied).toHaveBeenCalledTimes(1); expect(reads()).toHaveLength(2);
  view.rerender(wrap({ ...p, refreshSequence: '2' }));
  await waitFor(() => expect(screen.getAllByRole('listitem')).toHaveLength(50));
  expect(reads()).toHaveLength(3); expect(reads().at(-1)?.[0]).toBe(`/cards/${id(3)}/activity`);
  expect(p.onDenied).toHaveBeenCalledTimes(1);
});
it('excludes delayed replies from an old Board read generation', async () => {
  let release: (value: unknown) => void = () => {}; let calls = 0;
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : ++calls === 1 ? new Promise(resolve => { release = resolve; }) : page([]));
  const p = props(); const view = render(wrap(p)); fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await waitFor(() => expect(reads()).toHaveLength(1)); view.rerender(wrap({ ...p, refreshSequence: '2' }));
  await screen.findByText('No activity to review yet. Authorized changes will appear here.'); release(page());
  await waitFor(() => expect(screen.queryByRole('listitem')).toBeNull());
});
import { Button, Dialog } from '@mui/material';
it('returns keyboard focus to an available page action and then the activity opener', async () => {
  const first = page(Array.from({ length: 50 }, (_, index) => item(100 - index)), 'opaque');
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : path.includes('?after=') ? page([item(50)]) : first);
  render(wrap(props())); const opener = screen.getByRole('button', { name: 'Review Card activity' }); opener.focus(); fireEvent.click(opener);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Older activity' })).toHaveFocus());
  fireEvent.click(screen.getByRole('button', { name: 'Older activity' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Newer activity' })).toHaveFocus());
  const close = screen.getByRole('button', { name: 'Close activity' }); close.focus(); fireEvent.click(close);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review Card activity' })).toHaveFocus());
});
it('does not steal focus from another dialog control after a delayed page read', async () => {
  let release: (value: unknown) => void = () => {};
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : new Promise(resolve => { release = resolve; }));
  render(<MemoryRouter><Dialog open><ActivityHistoryControl {...props()} /><Button>Another control</Button></Dialog></MemoryRouter>);
  const opener = screen.getByRole('button', { name: 'Review Card activity' }); opener.focus(); fireEvent.click(opener);
  await waitFor(() => expect(reads()).toHaveLength(1)); const other = screen.getByRole('button', { name: 'Another control' }); other.focus(); release(page([]));
  await screen.findByText('No activity to review yet. Authorized changes will appear here.'); expect(other).toHaveFocus();
});
