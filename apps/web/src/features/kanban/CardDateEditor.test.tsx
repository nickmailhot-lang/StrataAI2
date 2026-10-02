import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardDateEditor } from './CardDateEditor';
import { workRequest, WorkRequestError, type WorkCard } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const profile = { id: '22222222-2222-2222-2222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const card: WorkCard = { id: 'card', title: 'Card', description: null, rank: 'rank', version: 3,
  startAt: null, dueAt: null, dueTimezone: null, dueHasTime: false, dueComplete: false };
const props = () => ({ card, organizationId: 'org', boardId: 'board', listId: 'list', editable: true, disabled: false, unavailable: false,
  onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const ack = () => ({ changed: true, card: { ...card, organizationId: 'org', boardId: 'board', listId: 'list', version: 4,
  dueAt: '2040-01-02T23:59:59.999999Z', dueTimezone: 'UTC' } });
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
async function edit() {
  fireEvent.click(screen.getByRole('button', { name: 'Edit dates' }));
  await screen.findByLabelText('Due date'); fireEvent.change(screen.getByLabelText('Due date'), { target: { value: '2040-01-02' } });
}
it('saves a scoped, versioned calendar date and returns focus after acknowledgment', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : ack());
  const p = props(); render(<CardDateEditor {...p} />); await edit();
  fireEvent.click(screen.getByRole('button', { name: 'Save dates' })); await screen.findByText('Dates saved.');
  const request = vi.mocked(workRequest).mock.calls.find(([path]) => path.endsWith('/dates'))!;
  expect(JSON.parse(request[1]!.body as string)).toEqual({ startAt: null, dueAt: '2040-01-02', dueTimezone: 'UTC', dueHasTime: false, dueComplete: false, version: 3 });
  expect((request[1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  expect(p.onRefresh).toHaveBeenCalled(); await waitFor(() => expect(screen.getByRole('button', { name: 'Clear dates' })).toHaveFocus());
});
it('retains identical input and retry key after lost acknowledgment despite a newer snapshot', async () => {
  let writes = 0;
  vi.mocked(workRequest).mockImplementation(async path => {
    if (path === '/me') return profile;
    if (++writes === 1) throw new WorkRequestError(0, null);
    return ack();
  });
  const p = props(); const { rerender } = render(<CardDateEditor {...p} />); await edit();
  fireEvent.click(screen.getByRole('button', { name: 'Save dates' })); await screen.findByRole('button', { name: 'Retry date save' });
  rerender(<CardDateEditor {...p} card={ack().card} />);
  expect(screen.getByLabelText('Due date')).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Clear dates' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Retry date save' })); await screen.findByText('Dates saved.');
  const requests = vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/dates')).map(([, options]) => options);
  expect(requests).toHaveLength(2); expect(requests[0]!.body).toBe(requests[1]!.body);
  expect(requests[0]!.headers).toEqual(requests[1]!.headers);
});
it('preserves dirty drafts on incoming Card revision and requires explicit discard', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile); const p = props(); const { rerender } = render(<CardDateEditor {...p} />); await edit();
  rerender(<CardDateEditor {...p} card={{ ...card, version: 4 }} />);
  expect(screen.getByLabelText('Due date')).toHaveValue('2040-01-02'); expect(screen.getByRole('button', { name: 'Save dates' })).toBeDisabled();
  expect(screen.getByRole('alert')).toHaveTextContent('draft is preserved');
  fireEvent.click(screen.getByRole('button', { name: 'Discard date edits and load latest' }));
  expect(screen.queryByLabelText('Due date')).not.toBeInTheDocument(); expect(p.onRefresh).toHaveBeenCalled();
});
it('refuses to submit a pending intent through a different current account', async () => {
  vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce({ ...profile, id: '33333333-3333-3333-3333-333333333333' });
  const p = props(); render(<CardDateEditor {...p} />); await edit(); fireEvent.click(screen.getByRole('button', { name: 'Save dates' }));
  await screen.findByText(/Date editing is unavailable/);
  expect(vi.mocked(workRequest).mock.calls.some(([path]) => path.endsWith('/dates'))).toBe(false);
  expect(p.onRefresh).toHaveBeenCalled(); expect(screen.getByRole('button', { name: 'Save dates' })).toBeDisabled();
});
it('disables editing after permission loss and hides fields during scope revalidation', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile); const p = props(); const { rerender } = render(<CardDateEditor {...p} />); await edit();
  rerender(<CardDateEditor {...p} editable={false} />); expect(screen.getByLabelText('Due date')).toBeDisabled();
  rerender(<CardDateEditor {...p} unavailable />); expect(screen.queryByLabelText('Due date')).not.toBeInTheDocument();
  expect(screen.getByText('Checking current Card dates…')).toBeInTheDocument();
});
