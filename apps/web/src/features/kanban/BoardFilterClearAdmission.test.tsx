import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { BoardFilterControl } from './BoardFilterControl';
import { AppThemeFixture } from '../../test/AppThemeFixture';
import type { BoardSnapshot } from '../../api/workManagement';

const actor = '11111111-1111-4111-8111-111111111111', org = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', list = '44444444-4444-4444-8444-444444444444';
const card = { id: '55555555-5555-4555-8555-555555555555', organizationId: org, boardId: board, listId: list,
  title: 'Inspect roof', description: null, rank: '500000000000000000000000000000', lifecycleState: 'active' as const, version: 1 };
const snapshot: BoardSnapshot = { board: { id: board, organizationId: org, name: 'Board', description: null, lifecycleState: 'active' },
  lists: [{ list: { id: list, name: 'Planning', rank: 'a', lifecycleState: 'active' }, cards: [card] }],
  access: { canView: true, canEdit: false, canAdminister: false, canMove: false } };
const response = (value: unknown) => new Response(JSON.stringify(value));
function setup() {
  const state = { writes: 0, keys: [] as string[], pauseLabels: false, paused: undefined as (() => void) | undefined,
    memberPaused: undefined as (() => void) | undefined };
  const labels = () => response({ organizationId: org, boardId: board, items: [], nextCursor: null });
  vi.stubGlobal('fetch', vi.fn(async (path: string, init?: RequestInit) => {
    if (path === '/me') return response({ id: actor });
    if (path.includes('/labels')) {
      if (state.pauseLabels) return new Promise<Response>(resolve => { state.paused = () => resolve(labels()); });
      return labels();
    }
    if (path.includes('/assignable-members')) return new Promise<Response>(resolve => {
      state.memberPaused = () => resolve(response({ organizationId: org, boardId: board, items: [], nextCursor: null }));
    });
    if (path.includes('/cards?')) return response({ organizationId: org, boardId: board, items: [card], nextCursor: null });
    if (path.includes('/filter-change?')) {
      expect(init?.method).toBe('POST'); state.writes++;
      const headers = new Headers(init?.headers), key = headers.get('Idempotency-Key')!;
      expect(headers.get('X-StrataAI-Expected-Actor')).toBe(actor); state.keys.push(key);
      return response({ eventId: key, eventType: 'BOARD_FILTER_CHANGED', actorId: actor, organizationId: org, boardId: board,
        entityType: 'BoardFilter', entityId: key, version: 1, metadata: {}, createdAt: '2026-10-05T12:00:00.123456Z' });
    }
    throw new Error('Unexpected request path');
  }));
  const onCanvasChange = vi.fn();
  render(<MemoryRouter><BoardFilterControl snapshot={snapshot} disabled={false} onRefresh={vi.fn()} onCanvasChange={onCanvasChange} /></MemoryRouter>, { wrapper: AppThemeFixture });
  return { state, onCanvasChange };
}
async function open() {
  fireEvent.click(screen.getByRole('button', { name: 'Filter Board Cards' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Apply filters' })).toBeEnabled());
}
beforeEach(() => sessionStorage.clear());
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

it('PRD-25-VS-02-T-03/AC-01: canvas Clear waits for foreground choices then accepts exactly one actor-bound command', async () => {
  const { state, onCanvasChange } = setup(); await open();
  fireEvent.change(screen.getByLabelText('Card keyword'), { target: { value: 'Inspect' } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await screen.findByRole('link', { name: 'Inspect roof — Planning' });
  fireEvent.click(screen.getByRole('button', { name: 'Show this page on Board' }));
  await screen.findByText('Filtered Board: 1 matching Cards on this page.');
  const clear = await screen.findByRole('button', { name: 'Clear Board filters' });
  await waitFor(() => expect(clear).toBeEnabled()); expect(state.writes).toBe(1);
  state.pauseLabels = true; fireEvent(window, new Event('focus'));
  await waitFor(() => expect(state.paused).toBeTypeOf('function'));
  try {
    expect(clear).toBeDisabled(); fireEvent.click(clear); expect(state.writes).toBe(1);
  } finally { await act(async () => { state.paused?.(); }); }
  await waitFor(() => expect(clear).toBeEnabled());
  fireEvent.click(clear);
  await waitFor(() => expect(onCanvasChange).toHaveBeenLastCalledWith(undefined));
  expect(state.writes).toBe(2); expect(new Set(state.keys).size).toBe(2);
  expect(sessionStorage.getItem(`strataai:board-filter:v1:${actor}:${org}:${board}`)).toBeNull();
});

it('PRD-25-VS-02-T-03/AC-01: dialog Clear cannot appear available while assignee admission refuses it', async () => {
  const { state } = setup(); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Choose assignees' }));
  await waitFor(() => expect(state.memberPaused).toBeTypeOf('function'));
  const clear = screen.getByRole('button', { name: 'Clear filters' });
  try {
    expect(clear).toBeDisabled(); fireEvent.click(clear); expect(state.writes).toBe(0);
  } finally { await act(async () => { state.memberPaused?.(); }); }
  await waitFor(() => expect(clear).toBeEnabled()); fireEvent.click(clear);
  await screen.findByText('Filter change acknowledged.'); expect(state.writes).toBe(1);
});
