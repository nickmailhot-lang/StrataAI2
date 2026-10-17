import type { ComponentProps } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardScreen } from './BoardScreen';
import { AppThemeFixture } from '../../test/AppThemeFixture';
const sibling = vi.hoisted(() => ({ release: undefined as (() => void) | undefined }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));
vi.mock('./LabelCreateControl', async importOriginal => {
  const actual = await importOriginal<typeof import('./LabelCreateControl')>();
  return { ...actual, LabelCreateControl: (props: ComponentProps<typeof actual.LabelCreateControl>) => {
    sibling.release = () => props.onBusyChange(false);
    return <actual.LabelCreateControl {...props} />;
  } };
});
const profile = { id: '11111111-1111-1111-1111-111111111111', status: 'ACTIVE', version: 1, locale: 'en', timezone: 'UTC', emailVerified: true };
const snapshot = { board: { id: 'board', organizationId: 'org', name: 'Admitted work', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: [{ list: { id: 'list', name: 'Planning', rank: 'a', lifecycleState: 'active' }, cards: [] }] };
const reply = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
afterEach(() => { vi.unstubAllGlobals(); sibling.release = undefined; });
it.each(['List', 'Card'] as const)('PRD-07-VS-05-T-03 keeps %s admission pending after a sibling busy release', async kind => {
  let finish!: (value: Response) => void;
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me'
    ? new Promise<Response>(resolve => { finish = resolve; }) : reply(snapshot));
  vi.stubGlobal('fetch', fetcher);
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> }], { initialEntries: ['/app/org/boards/board'] });
  render(<RouterProvider router={router} />, { wrapper: AppThemeFixture });
  const name = kind === 'List' ? 'Add list' : 'Add card to Planning';
  const launcher = await screen.findByRole('button', { name, exact: true });
  await waitFor(() => expect(launcher).toBeEnabled()); fireEvent.click(launcher);
  await waitFor(() => expect(finish).toBeDefined()); expect(sibling.release).toBeDefined();
  act(() => sibling.release!());
  expect(screen.getByRole('button', { name, exact: true })).toBeDisabled();
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(fetcher.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(0);
  await act(async () => finish(reply(profile)));
  expect(await screen.findByRole('dialog', { name: `Add ${kind.toLowerCase()}`, exact: true })).toBeVisible();
  expect(fetcher.mock.calls.filter(([path]) => path === '/me')).toHaveLength(1);
});
