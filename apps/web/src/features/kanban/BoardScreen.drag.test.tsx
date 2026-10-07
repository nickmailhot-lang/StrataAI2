import type { ComponentProps } from 'react';
import type { DndContext, DragEndEvent, DragStartEvent } from '@dnd-kit/core';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardScreen } from './BoardScreen';
import type { BoardSnapshot } from '../../api/workManagement';

const drag = vi.hoisted(() => ({ current: undefined as ComponentProps<typeof DndContext> | undefined }));
const canvas = vi.hoisted(() => ({ items: undefined as unknown[] | undefined }));
vi.mock('./BoardWindow', async importOriginal => {
  const actual = await importOriginal<typeof import('./BoardWindow')>();
  return { ...actual, BoardWindow: (props: ComponentProps<typeof actual.BoardWindow>) => {
    if (props.axis === 'lists') canvas.items = props.items;
    return <actual.BoardWindow {...props} />;
  } };
});
vi.mock('@dnd-kit/core', async importOriginal => {
  const actual = await importOriginal<typeof import('@dnd-kit/core')>();
  return { ...actual, DndContext: (props: ComponentProps<typeof DndContext>) => {
    drag.current = props;
    return <actual.DndContext {...props} />;
  } };
});
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => () => {}) }));

const rank = '500000000000000000000000000000';
const card = { id: 'card', title: 'Inspect roof', description: null, rank, version: 3 };
const snapshot: BoardSnapshot = {
  board: { id: 'board', organizationId: 'org', name: 'Repairs', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: [
    { list: { id: 'source', name: 'Planning', rank, version: 1, lifecycleState: 'active' }, cards: [card] },
    { list: { id: 'dest', name: 'Complete', rank, version: 1, lifecycleState: 'active' }, cards: [] },
  ],
};
const reply = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
afterEach(() => { vi.unstubAllGlobals(); drag.current = undefined; canvas.items = undefined; });

it('opens cached Card detail and retires the modal with focus restored to its canvas link', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => reply(snapshot)));
    const router = createMemoryRouter([
      { path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> },
      { path: '/app/:organizationId/boards/:boardId/cards/:cardId', element: <BoardScreen /> },
    ], { initialEntries: ['/app/org/boards/board'] });
    render(<RouterProvider router={router} />);
    await waitFor(() => expect(screen.getByRole('button', { name: `Drag ${card.title} card` })).toBeEnabled());
    act(() => screen.getByRole('link', { name: card.title }).click());
    await screen.findByRole('textbox', { name: 'Card title' });
    expect(screen.getByRole('textbox', { name: 'Card title' })).toBeEnabled();
    act(() => screen.getByRole('button', { name: 'Close' }).click());
    await waitFor(() => expect(screen.getByRole('link', { name: card.title })).toHaveFocus());
});

it('preserves canvas columns during dialog state changes and replaces them after an authoritative refresh', async () => {
  let current = snapshot;
  vi.stubGlobal('fetch', vi.fn(async () => reply(current)));
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> }],
    { initialEntries: ['/app/org/boards/board'] });
  render(<RouterProvider router={router} />);
  await waitFor(() => expect(screen.getByRole('button', { name: `Drag ${card.title} card` })).toBeEnabled());
  const original = canvas.items;
  act(() => screen.getByRole('button', { name: 'Add list' }).click());
  expect(screen.getByRole('dialog')).toBeVisible();
  expect(canvas.items).toBe(original);
  act(() => within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }).click());
  expect(canvas.items).toBe(original);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  current = { ...snapshot, lists: snapshot.lists.map(column => ({ ...column,
    cards: column.cards.map(item => ({ ...item, title: 'Authoritative new title', version: item.version + 1 })) })) };
  act(() => screen.getByRole('button', { name: 'Refresh board' }).click());
  await screen.findByRole('link', { name: 'Authoritative new title' });
  expect(canvas.items).not.toBe(original);
  expect(snapshot.lists[0].cards[0].title).toBe(card.title);
});

it('renders provisional drop placement before starting persistence and rolls it back on an uncertain result', async () => {
  let reject!: (reason: Error) => void;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (options?.method === 'POST') {
      // This callback runs from the mounted command control. The drop event's
      // first commit must already show the destination, without awaiting it.
      expect(within(screen.getByRole('region', { name: 'Complete' })).getByRole('link', { name: card.title })).toBeVisible();
      expect(within(screen.getByRole('region', { name: 'Planning' })).queryByRole('link', { name: card.title })).not.toBeInTheDocument();
      return new Promise<Response>((_, failed) => { reject = failed; });
    }
    expect(path).toBe('/boards/board');
    return reply(snapshot);
  });
  vi.stubGlobal('fetch', fetcher);
  const router = createMemoryRouter([{ path: '/app/:organizationId/boards/:boardId', element: <BoardScreen /> }],
    { initialEntries: ['/app/org/boards/board'] });
  render(<RouterProvider router={router} />);
  await waitFor(() => expect(screen.getByRole('button', { name: `Drag ${card.title} card` })).toBeEnabled());
  const handle = screen.getByRole('button', { name: `Drag ${card.title} card` });
  const activatorEvent = new MouseEvent('pointerdown', { bubbles: true });
  handle.dispatchEvent(activatorEvent);
  const active: DragStartEvent['active'] = { id: 'card:card', data: { current: {} },
    rect: { current: { initial: null, translated: null } } };
  act(() => drag.current!.onDragStart!({ active, activatorEvent }));
  act(() => drag.current!.onDragEnd!({ active: { id: 'card:card' }, over: { id: 'card-end:dest' } } as DragEndEvent));
  // The command publishes after mount replay retires. The fetch callback above
  // still proves provisional placement is visible before persistence starts.
  await waitFor(() => expect(fetcher.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1));
  expect(JSON.parse(fetcher.mock.calls.find(call => call[1]?.method === 'POST')![1]!.body as string))
    .toEqual({ destinationListId: 'dest', expectedVersion: 3 });
  await act(async () => reject(new Error('Unknown result')));
  await screen.findByRole('button', { name: 'Retry this move' });
  expect(within(screen.getByRole('region', { name: 'Planning' })).getByRole('link', { name: card.title })).toBeVisible();
  expect(within(screen.getByRole('region', { name: 'Complete' })).queryByRole('link', { name: card.title })).not.toBeInTheDocument();
  expect(snapshot.lists[0].cards).toEqual([card]);
  expect(snapshot.lists[1].cards).toEqual([]);
});
