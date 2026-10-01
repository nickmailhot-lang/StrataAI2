import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardMembersPage } from './BoardMembersPage';
import { watchBoard } from '../../api/boardLive';
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
const org = '10000000-0000-4000-8000-000000000001', board = '20000000-0000-4000-8000-000000000002';
const row = { boardId: board, userId: '30000000-0000-4000-8000-000000000003', role: 'MEMBER', active: true, version: 4,
  displayName: 'Jordan', email: 'jordan@example.test', organizationMemberActive: true };
const scope = { board: { id: board, organizationId: org, name: 'Private repairs', lifecycleState: 'active' }, access: { canAdminister: true } };
const reply = (value: unknown, status = 200, cursor?: string) => new Response(status === 204 ? null : JSON.stringify(value),
  { status, headers: cursor ? { 'X-StrataAI-Next-Cursor': cursor } : {} });
const participant = (n: number) => ({ ...row, userId: `40000000-0000-4000-8000-${String(n).padStart(12, '0')}`,
  displayName: `Participant ${n}`, email: `participant-${n}@example.test` });
const firstPage = Array.from({ length: 50 }, (_, n) => participant(n + 1));
const firstCursor = firstPage.at(-1)!.userId;
function mount(...responses: (Response | Error)[]) {
  const mock = vi.fn(); for (const r of responses) { if (r instanceof Error) mock.mockRejectedValueOnce(r); else mock.mockResolvedValueOnce(r); }
  vi.stubGlobal('fetch', mock);
  render(<RouterProvider router={createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/members', element: <BoardMembersPage /> }],
    { initialEntries: [`/app/${org}/boards/${board}/members`] })} />); return mock;
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
it('describes same-name member actions with the correct profile and role without exposing former profiles', async () => {
  const second = { ...participant(1), displayName: row.displayName };
  const former = { ...participant(2), displayName: null, email: null, organizationMemberActive: false };
  mount(reply(scope), reply([row, second, former]));
  const buttons = await screen.findAllByRole('button', { name: 'Make administrator: Jordan' });
  expect(buttons[0]).toHaveAccessibleDescription('jordan@example.test Board access: member');
  expect(buttons[1]).toHaveAccessibleDescription('participant-1@example.test Board access: member');
  const articles = screen.getAllByRole('article', { name: 'Jordan' });
  expect(articles[0]).toHaveAccessibleDescription('jordan@example.test Board access: member');
  expect(articles[1]).toHaveAccessibleDescription('participant-1@example.test Board access: member');
  expect(screen.getByRole('button', { name: `Remove from Board: ${former.userId}` }))
    .toHaveAccessibleDescription(`Member reference: ${former.userId} Board access: member`);
  expect(screen.queryByText('participant-2@example.test')).not.toBeInTheDocument();
});
it('retries the requested next page after a failed read instead of silently returning to the previous page', async () => {
  const mock = mount(reply(scope), reply(firstPage, 200, firstCursor), reply({}, 503), reply(scope), reply([participant(51)]));
  await screen.findByRole('heading', { name: 'Participant 1' });
  fireEvent.click(screen.getByRole('button', { name: 'Next members' }));
  await screen.findByText(/Unable to confirm current Board members/);
  expect(screen.queryByRole('heading', { name: 'Participant 1' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Check current members' }));
  await screen.findByRole('heading', { name: 'Participant 51' });
  expect(mock.mock.calls[4][0]).toBe(`/boards/${board}/members?after=${firstCursor}`);
  expect(screen.getByRole('button', { name: 'Previous members' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Next members' })).toBeDisabled();
}, 10_000);
it('preserves the selected page through live refresh and uses the prior cursor when navigating back', async () => {
  let invalidate: (() => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const mock = mount(reply(scope), reply(firstPage, 200, firstCursor), reply(scope), reply([participant(51)]),
    reply(scope), reply([{ ...participant(51), role: 'ADMIN', version: 5 }]), reply(scope), reply(firstPage, 200, firstCursor));
  await screen.findByRole('heading', { name: 'Participant 1' });
  await waitFor(() => expect(invalidate).toBeDefined());
  fireEvent.click(screen.getByRole('button', { name: 'Next members' }));
  await screen.findByRole('heading', { name: 'Participant 51' }); act(() => invalidate!());
  await screen.findByRole('button', { name: 'Make member: Participant 51' });
  expect(mock.mock.calls[3][0]).toBe(`/boards/${board}/members?after=${firstCursor}`);
  expect(mock.mock.calls[5][0]).toBe(`/boards/${board}/members?after=${firstCursor}`);
  expect(watchBoard).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole('button', { name: 'Previous members' }));
  await screen.findByRole('heading', { name: 'Participant 1' });
  expect(mock.mock.calls[7][0]).toBe(`/boards/${board}/members`);
  expect(screen.getByRole('button', { name: 'Previous members' })).toBeDisabled();
  expect(mock.mock.calls.filter(call => ['PATCH', 'DELETE'].includes(call[1]?.method))).toHaveLength(0);
}, 10_000);
it('rejects a cursor that does not bind to the last bounded row before disclosing any profiles', async () => {
  mount(reply(scope), reply(firstPage, 200, firstPage[0].userId));
  await screen.findByText(/Unable to confirm current Board members/);
  expect(screen.queryByText('Private repairs')).not.toBeInTheDocument();
  expect(screen.queryByText('participant-1@example.test')).not.toBeInTheDocument();
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
  expect(watchBoard).not.toHaveBeenCalled();
});
it('coalesces events during a pending directory refresh into one follow-up read', async () => {
  let invalidate: (() => void) | undefined; let release: ((value: Response) => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const mock = mount(reply(scope), reply([row]));
  await screen.findByRole('button', { name: 'Make administrator: Jordan' });
  await waitFor(() => expect(invalidate).toBeDefined());
  mock.mockImplementationOnce(() => new Promise<Response>(resolve => { release = resolve; }))
    .mockResolvedValueOnce(reply([row])).mockResolvedValueOnce(reply(scope))
    .mockResolvedValueOnce(reply([{ ...row, role: 'ADMIN', version: 5 }]));
  act(() => { invalidate!(); invalidate!(); invalidate!(); invalidate!(); });
  expect(mock).toHaveBeenCalledTimes(3);
  await act(async () => { release!(reply(scope)); });
  await screen.findByRole('button', { name: 'Make member: Jordan' });
  expect(mock).toHaveBeenCalledTimes(6); expect(watchBoard).toHaveBeenCalledTimes(1);
  expect(mock.mock.calls.filter(call => ['PATCH', 'DELETE'].includes(call[1]?.method))).toHaveLength(0);
});
it('retries a failed live read without another event or automatic mutation', async () => {
  let invalidate: (() => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const mock = mount(reply(scope), reply([row]), reply({}, 503), reply(scope), reply([{ ...row, role: 'ADMIN', version: 5 }]));
  await screen.findByRole('button', { name: 'Make administrator: Jordan' });
  await waitFor(() => expect(invalidate).toBeDefined());
  vi.useFakeTimers();
  await act(async () => { invalidate!(); await vi.advanceTimersByTimeAsync(0); });
  expect(screen.getByText(/Unable to confirm current Board members/)).toBeInTheDocument();
  expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
  expect(screen.getByRole('button', { name: 'Make member: Jordan' })).toBeInTheDocument();
  expect(screen.queryByText(/Unable to confirm current Board members/)).not.toBeInTheDocument();
  expect(mock).toHaveBeenCalledTimes(5); expect(watchBoard).toHaveBeenCalledTimes(1);
  expect(mock.mock.calls.filter(call => ['PATCH', 'DELETE'].includes(call[1]?.method))).toHaveLength(0);
});
it('discards queued refreshes when current read authorization is denied', async () => {
  let invalidate: (() => void) | undefined; let release: ((value: Response) => void) | undefined; const dispose = vi.fn();
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return dispose; });
  const mock = mount(reply(scope), reply([row]));
  await screen.findByRole('button', { name: 'Make administrator: Jordan' });
  await waitFor(() => expect(invalidate).toBeDefined());
  mock.mockImplementationOnce(() => new Promise<Response>(resolve => { release = resolve; }));
  act(() => { invalidate!(); invalidate!(); invalidate!(); });
  await act(async () => { release!(reply({}, 403)); });
  await screen.findByText('Board member administration is unavailable.');
  await waitFor(() => expect(dispose).toHaveBeenCalledTimes(1));
  expect(mock).toHaveBeenCalledTimes(3); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
});
it('refreshes a changed role from live invalidation without restarting the subscription', async () => {
  let invalidate: (() => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  mount(reply(scope), reply([row]), reply(scope), reply([{ ...row, role: 'ADMIN', version: 5 }]));
  await screen.findByRole('button', { name: 'Make administrator: Jordan' });
  await waitFor(() => expect(invalidate).toBeDefined());
  act(() => invalidate!());
  await screen.findByRole('button', { name: 'Make member: Jordan' });
  expect(watchBoard).toHaveBeenCalledTimes(1);
});
it('invalidates open consent and clears protected data when live refresh discovers revoked administration', async () => {
  let invalidate: (() => void) | undefined; const dispose = vi.fn();
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return dispose; });
  const mock = mount(reply(scope), reply([row]), reply({}, 403));
  fireEvent.click(await screen.findByRole('button', { name: 'Make administrator: Jordan' }));
  await waitFor(() => expect(invalidate).toBeDefined()); act(() => invalidate!());
  await screen.findByText('Board member administration is unavailable.');
  expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Private repairs')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(dispose).toHaveBeenCalledTimes(1));
  expect(mock.mock.calls.filter(call => ['PATCH', 'DELETE'].includes(call[1]?.method))).toHaveLength(0);
});
it('requires consent and sends the reviewed member version before confirming a bound role change', async () => {
  const next = { ...row, role: 'ADMIN', version: 5 };
  const mock = mount(reply(scope), reply([row]), reply(next), reply(scope), reply([next]));
  fireEvent.click(await screen.findByRole('button', { name: 'Make administrator: Jordan' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus());
  expect(mock).toHaveBeenCalledTimes(2);
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member change' }));
  await screen.findByText('Board access: admin');
  expect(mock.mock.calls[2][1].headers.get('If-Match')).toBe('"4"');
  expect(mock.mock.calls[2][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(mock.mock.calls[2][1].headers.get('X-StrataAI-Request')).toBe('1');
  expect(JSON.parse(mock.mock.calls[2][1].body)).toEqual({ role: 'ADMIN' });
});
it('rejects a foreign Board row before exposing profiles', async () => {
  mount(reply(scope), reply([{ ...row, boardId: org }])); await screen.findByText(/Unable to confirm current Board members/);
  expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Private repairs')).not.toBeInTheDocument();
});
it('clears private metadata on lost authority during removal', async () => {
  mount(reply(scope), reply([row]), reply({ code: 'board_not_found' }, 404));
  fireEvent.click(await screen.findByRole('button', { name: 'Remove from Board: Jordan' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member change' }));
  await screen.findByText('Board member administration is unavailable.');
  expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Private repairs')).not.toBeInTheDocument();
});
it('recovers a lost removal response by read without assuming the missing row proves this operation succeeded', async () => {
  const mock = mount(reply(scope), reply([row]), new Error('Lost response'), reply(scope), reply([]));
  fireEvent.click(await screen.findByRole('button', { name: 'Remove from Board: Jordan' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member change' }));
  await screen.findByText(/The member change could not be confirmed/);
  fireEvent.click(await screen.findByRole('button', { name: 'Check current members' }));
  await screen.findByText('No active Board memberships on this page.');
  expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  expect(screen.queryByText(/Member change acknowledged/)).not.toBeInTheDocument();
});
it('retains an uncertain-write warning during automatic live recovery without repeating the command', async () => {
  let invalidate: (() => void) | undefined;
  vi.mocked(watchBoard).mockImplementationOnce(options => { invalidate = options.invalidate; return () => {}; });
  const mock = mount(reply(scope), reply([row]), new Error('Lost response'), reply(scope), reply([]));
  fireEvent.click(await screen.findByRole('button', { name: 'Remove from Board: Jordan' }));
  await waitFor(() => expect(invalidate).toBeDefined());
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member change' }));
  await screen.findByText(/The member change could not be confirmed/);
  act(() => invalidate!());
  await screen.findByText('No active Board memberships on this page.');
  expect(screen.getByText(/The member change could not be confirmed/)).toBeInTheDocument();
  expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  expect(screen.queryByText(/Member change acknowledged/)).not.toBeInTheDocument();
});
