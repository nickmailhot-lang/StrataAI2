import { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardDatePolicyPage } from './BoardDatePolicyPage';
import { workRequest, WorkRequestError } from '../../api/workManagement';
const live = vi.hoisted(() => ({ invalidate: undefined as (() => void) | undefined }));
vi.mock('../../api/workManagement', async original => ({ ...await original<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn((options: { invalidate: () => void }) => { live.invalidate = options.invalidate; return () => undefined; }) }));
const org = '11111111-1111-1111-1111-111111111111', id = '22222222-2222-2222-2222-222222222222';
const profile = { id: '33333333-3333-3333-3333-333333333333', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const board = { id, organizationId: org, name: 'Policy Board', version: 4, lifecycleState: 'active', dateTimezoneOverride: null as string | null };
const scope = (value = board) => ({ board: value, access: { canAdminister: true } });
const mock = vi.mocked(workRequest);
function mount() { return render(<RouterProvider router={createMemoryRouter([
  { path: '/app/:organizationId/boards/:boardId/date-policy', element: <BoardDatePolicyPage /> },
], { initialEntries: [`/app/${org}/boards/${id}/date-policy`] })} />); }
async function choose() {
  fireEvent.change(await screen.findByRole('textbox', { name: 'Board timezone override' }), { target: { value: 'Pacific/Honolulu' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save timezone policy' }));
}
beforeEach(() => { mock.mockReset(); live.invalidate = undefined; });
afterEach(() => vi.useRealTimers());
it('validates administrator scope and rechecks the actor before a versioned policy save', async () => {
  const next = { ...board, version: 5, dateTimezoneOverride: 'Pacific/Honolulu' };
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()).mockResolvedValueOnce(profile)
    .mockResolvedValueOnce(scope()).mockResolvedValueOnce({ board: next, changed: true })
    .mockResolvedValueOnce(profile).mockResolvedValueOnce(scope(next));
  mount(); await choose();
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Board timezone override' })).toHaveValue('Pacific/Honolulu'));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Save timezone policy' })).toHaveFocus());
  expect(mock.mock.calls[4][0]).toBe(`/boards/${id}/date-policy`);
  expect(JSON.parse(mock.mock.calls[4][1]!.body as string)).toEqual({ timezone: 'Pacific/Honolulu', version: 4 });
  expect((mock.mock.calls[4][1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
});
it('retains the original body and key after uncertainty and reloads newer policy after the original acknowledgment', async () => {
  const oldAck = { ...board, version: 5, dateTimezoneOverride: 'Pacific/Honolulu' };
  const latest = { ...board, version: 8, dateTimezoneOverride: 'UTC' };
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope())
    .mockRejectedValueOnce(new Error('lost response')).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope(latest))
    .mockResolvedValueOnce({ board: oldAck, changed: true }).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope(latest));
  mount(); await choose(); const retry = await screen.findByRole('button', { name: 'Retry timezone change' });
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Check current timezone policy' })).not.toBeInTheDocument();
  act(() => live.invalidate!()); expect(mock).toHaveBeenCalledTimes(5);
  fireEvent.click(retry);
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Board timezone override' })).toHaveValue('UTC'));
  expect(mock.mock.calls[7][1]!.body).toBe(mock.mock.calls[4][1]!.body);
  expect(mock.mock.calls[7][1]!.headers).toEqual(mock.mock.calls[4][1]!.headers);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Save timezone policy' })).toHaveFocus());
});
it('cannot retry the original change under a different account', async () => {
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope())
    .mockRejectedValueOnce(new Error('lost response')).mockResolvedValueOnce({ ...profile, id: org });
  mount(); await choose(); fireEvent.click(await screen.findByRole('button', { name: 'Retry timezone change' }));
  await screen.findByText(/Board timezone administration is unavailable/);
  expect(mock).toHaveBeenCalledTimes(6); expect(screen.queryByText('Policy Board')).not.toBeInTheDocument();
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
});
it('removes stale drafts on live invalidation and loads the current policy', async () => {
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()).mockResolvedValueOnce(profile)
    .mockResolvedValueOnce(scope({ ...board, version: 5, dateTimezoneOverride: 'UTC' }));
  mount(); fireEvent.change(await screen.findByRole('textbox'), { target: { value: 'Pacific/Honolulu' } });
  act(() => live.invalidate!()); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveValue('UTC')); expect(mock).toHaveBeenCalledTimes(4);
});
it('a conflict requires fresh settings before another versioned command', async () => {
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope())
    .mockRejectedValueOnce(new WorkRequestError(409, 'version_conflict')).mockResolvedValueOnce(profile)
    .mockResolvedValueOnce(scope({ ...board, version: 7, dateTimezoneOverride: 'UTC' }));
  mount(); await choose(); await screen.findByText(/policy change is unavailable/);
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Check current timezone policy' }));
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveValue('UTC'));
});
it('rejects invalid timezone input without starting a new admission or command', async () => {
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()); mount();
  fireEvent.change(await screen.findByRole('textbox'), { target: { value: 'Unknown/Place' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save timezone policy' }));
  await screen.findByText(/Enter a valid IANA timezone/); expect(mock).toHaveBeenCalledTimes(2);
});
it('clears a shared policy with an explicit null and the current Board revision', async () => {
  const configured = { ...board, dateTimezoneOverride: 'UTC' }, cleared = { ...board, version: 5 };
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope(configured)).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope(configured))
    .mockResolvedValueOnce({ board: cleared, changed: true }).mockResolvedValueOnce(profile).mockResolvedValueOnce(scope(cleared));
  mount(); fireEvent.change(await screen.findByRole('textbox'), { target: { value: '' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save timezone policy' }));
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveValue(''));
  expect(JSON.parse(mock.mock.calls[4][1]!.body as string)).toEqual({ timezone: null, version: 4 });
});
it('revocation during fresh command admission prevents a policy write and removes protected names', async () => {
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(scope()).mockResolvedValueOnce(profile)
    .mockRejectedValueOnce(new WorkRequestError(403, 'access_denied'));
  mount(); await choose(); await screen.findByText(/Board timezone administration is unavailable/);
  expect(mock).toHaveBeenCalledTimes(4); expect(screen.queryByText('Policy Board')).not.toBeInTheDocument();
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
});
it('a bounded stalled profile read recovers without a late Board request', async () => {
  vi.useFakeTimers(); let resolve!: (value: unknown) => void;
  mock.mockImplementationOnce(() => new Promise(done => { resolve = done; })); mount();
  await act(async () => {}); expect(mock).toHaveBeenCalledTimes(1);
  await act(async () => vi.advanceTimersByTimeAsync(15_001));
  expect(screen.getByText(/Unable to load current Board timezone settings/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Check current timezone policy' })).toBeEnabled();
  await act(async () => resolve(profile)); expect(mock).toHaveBeenCalledTimes(1);
});
it.each([
  { board, access: { canAdminister: false } }, scope({ ...board, organizationId: id }),
  scope({ ...board, id: org }), scope({ ...board, lifecycleState: 'archived' }),
  scope({ ...board, dateTimezoneOverride: 'Unknown/Place' }), scope({ ...board, version: 0 }),
])('does not display a malformed or unadmitted Board scope %#', async value => {
  mock.mockResolvedValueOnce(profile).mockResolvedValueOnce(value); mount();
  await waitFor(() => expect(mock).toHaveBeenCalledTimes(2));
  await screen.findByText(/unavailable|Unable to load current/);
  expect(screen.queryByText('Policy Board')).not.toBeInTheDocument(); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
});
it('retiring the page aborts a late profile chain before Board discovery starts', async () => {
  let resolve!: (value: unknown) => void;
  mock.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  const view = mount(); await waitFor(() => expect(mock).toHaveBeenCalledTimes(1));
  const signal = mock.mock.calls[0][1]!.signal!; view.unmount(); expect(signal.aborted).toBe(true);
  await act(async () => resolve(profile)); expect(mock).toHaveBeenCalledTimes(1);
});
it('admits the policy after StrictMode retires its first pending profile read', async () => {
  let resolve!: (value: unknown) => void;
  mock.mockImplementationOnce(() => new Promise(done => { resolve = done; }))
    .mockResolvedValueOnce(profile).mockResolvedValueOnce(scope());
  render(<StrictMode><RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/boards/:boardId/date-policy', element: <BoardDatePolicyPage /> },
  ], { initialEntries: [`/app/${org}/boards/${id}/date-policy`] })} /></StrictMode>);
  await screen.findByRole('textbox', { name: 'Board timezone override' });
  expect(mock.mock.calls[0][1]!.signal!.aborted).toBe(true);
  expect(mock).toHaveBeenCalledTimes(3);
  await act(async () => resolve({ ...profile, id: org }));
  expect(mock).toHaveBeenCalledTimes(3);
  expect(screen.getByRole('textbox')).toHaveValue('');
});
