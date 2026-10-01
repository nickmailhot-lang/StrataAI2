import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardVisibilityPage } from './BoardVisibilityPage';
const live = vi.hoisted(() => ({ invalidate: undefined as (() => void) | undefined, dispose: vi.fn() }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn((options: { invalidate: () => void }) => {
  live.invalidate = options.invalidate; return live.dispose;
}) }));
beforeEach(() => { live.invalidate = undefined; live.dispose.mockClear(); });
const board = { id: 'b', organizationId: 'o', name: 'Private repairs', lifecycleState: 'active', visibility: 'PRIVATE', version: 4 };
const scope = { board, access: { canAdminister: true } };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount(...responses: Response[]) {
  const fetcher = vi.fn(); for (const r of responses) fetcher.mockResolvedValueOnce(r); vi.stubGlobal('fetch', fetcher);
  render(<RouterProvider router={createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId/visibility', element: <BoardVisibilityPage /> }],
    { initialEntries: ['/app/o/boards/b/visibility'] })} />); return fetcher;
}
async function choose() {
  fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Board visibility' }));
  fireEvent.click(await screen.findByRole('option', { name: 'Public' }));
  fireEvent.click(screen.getByRole('button', { name: 'Review visibility change' }));
  await screen.findByRole('dialog');
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });
it('requires explicit confirmation and sends the current version with a retry key', async () => {
  const next = { ...board, visibility: 'PUBLIC', version: 5 };
  const mock = mount(response(scope), response(next), response({ ...scope, board: next }));
  await choose(); await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus());
  expect(screen.queryByRole('textbox', { name: 'Public Board link' })).not.toBeInTheDocument();
  expect(mock).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole('button', { name: 'Confirm visibility change' }));
  await screen.findByText('Visibility change acknowledged. Current visibility loaded.');
  expect(await screen.findByRole('textbox', { name: 'Public Board link' })).toHaveValue(`${window.location.origin}/app/o/boards/b`);
  expect(screen.getByRole('textbox', { name: 'Public Board link' })).toHaveAttribute('readonly');
  expect(mock.mock.calls[1][0]).toBe('/boards/b/visibility');
  expect(JSON.parse(mock.mock.calls[1][1].body)).toEqual({ visibility: 'PUBLIC', version: 4 });
  expect(mock.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(mock.mock.calls[1][1].headers.get('X-StrataAI-Request')).toBe('1');
});
it('removes the public link when current visibility is narrowed or its read fails', async () => {
  mount(response({ ...scope, board: { ...board, visibility: 'PUBLIC' } }), response(scope), response({}, 503));
  await screen.findByRole('textbox', { name: 'Public Board link' });
  act(() => live.invalidate!());
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Board visibility' })).toHaveTextContent('Private'));
  expect(screen.queryByRole('textbox', { name: 'Public Board link' })).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Open public Board' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Check current visibility' }));
  await screen.findByText(/Unable to confirm current Board visibility/);
  expect(screen.queryByRole('textbox', { name: 'Public Board link' })).not.toBeInTheDocument();
});
it('rejects foreign scope before disclosing Board metadata', async () => {
  mount(response({ ...scope, board: { ...board, organizationId: 'other' } }));
  await screen.findByText('Board visibility administration is unavailable.');
  expect(screen.queryByText(board.name)).not.toBeInTheDocument();
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
});
it('clears stale state on conflict and checks the canonical state without another mutation', async () => {
  const mock = mount(response(scope), response({ code: 'version_conflict' }, 409), response(scope));
  await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm visibility change' }));
  await screen.findByText('The Board changed. Check current visibility before making another change.');
  expect(screen.queryByRole('heading', { name: board.name })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Check current visibility' }));
  await screen.findByRole('combobox');
  expect(mock.mock.calls.filter(call => call[1]?.method === 'PATCH')).toHaveLength(1);
});

it('cancels stale consent on live invalidation and loads the new canonical version without writing', async () => {
  const mock = mount(response(scope), response({ ...scope, board: { ...board, visibility: 'ORGANIZATION', version: 5 } }));
  await choose(); await waitFor(() => expect(live.invalidate).toBeDefined());
  act(() => live.invalidate!());
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Board visibility' })).toHaveTextContent('Organization'));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Check current visibility' })).toHaveFocus());
  expect(mock).toHaveBeenCalledTimes(2);
  expect(mock.mock.calls.filter(call => call[1]?.method === 'PATCH')).toHaveLength(0);
});
it('clears private metadata and stops live updates when current administration is revoked', async () => {
  mount(response(scope), response({}, 403));
  await choose(); await waitFor(() => expect(live.invalidate).toBeDefined()); act(() => live.invalidate!());
  await screen.findByText('Board visibility administration is unavailable.');
  expect(screen.queryByText(board.name)).not.toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(live.dispose).toHaveBeenCalledTimes(1));
});
it('retains conflict information when live recovery loads the current version without repeating a write', async () => {
  const mock = mount(response(scope), response({ code: 'version_conflict' }, 409),
    response({ ...scope, board: { ...board, visibility: 'ORGANIZATION', version: 5 } }));
  await choose(); await waitFor(() => expect(live.invalidate).toBeDefined());
  fireEvent.click(screen.getByRole('button', { name: 'Confirm visibility change' }));
  await screen.findByText('The Board changed. Check current visibility before making another change.');
  act(() => live.invalidate!());
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Board visibility' })).toHaveTextContent('Organization'));
  expect(screen.getByText('The Board changed. Check current visibility before making another change.')).toBeInTheDocument();
  expect(mock.mock.calls.filter(call => call[1]?.method === 'PATCH')).toHaveLength(1);
});
