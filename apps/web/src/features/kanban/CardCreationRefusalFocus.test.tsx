import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardScreen } from './BoardScreen';
import { AppThemeFixture } from '../../test/AppThemeFixture';

vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));

const actor = '11111111-1111-1111-1111-111111111111';
const profile = { id: actor, status: 'ACTIVE', version: 1, locale: 'en', timezone: 'UTC', emailVerified: true };
const snapshot = {
  board: { id: 'board', organizationId: 'org', name: 'Private work', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: [{ list: { id: 'list', name: 'Planning', rank: 'a', lifecycleState: 'active' }, cards: [] }],
};
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

afterEach(() => vi.unstubAllGlobals());

it.each([401, 403, 404])('PRD-08-VS-04-T-02/AC-01: retains modal-owned focus for a %i creation refusal', async status => {
  let finish!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    if (input === '/me') return reply(profile);
    if (options?.method === 'POST') return new Promise<Response>(resolve => { finish = resolve; });
    return reply(snapshot);
  }));
  const router = createMemoryRouter([
    { path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> },
  ], { initialEntries: ['/app/org/boards/board'] });
  render(<RouterProvider router={router} />, { wrapper: AppThemeFixture });
  fireEvent.click(await screen.findByRole('button', { name: 'Add card to Planning' }));
  const title = await screen.findByLabelText(/Card title/);
  fireEvent.change(title, { target: { value: 'Original Card draft' } });
  const dialog = screen.getByRole('dialog', { name: 'Add card' });
  const submit = within(dialog).getByRole('button', { name: /^Create$/ });
  fireEvent.click(submit);
  await waitFor(() => expect(finish).toBeTypeOf('function'));
  expect(submit).toBeDisabled();

  // The modal focus trap can hold its container while the form is busy.
  dialog.tabIndex = -1;
  dialog.focus();
  expect(dialog).toHaveFocus();
  expect(dialog.querySelector('form')?.contains(document.activeElement)).toBe(false);

  await act(async () => finish(reply({}, status)));
  const refusal = await screen.findByRole('alert');
  expect(refusal).toBeVisible();
  expect(refusal).toHaveFocus();
  expect(screen.queryByRole('dialog', { name: 'Add card' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private work')).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/Card title/)).not.toBeInTheDocument();
});
