import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { workRequest, WorkRequestError } from '../../api/workManagement';
import { GlobalSearchPage } from './GlobalSearchPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';
vi.mock('../../api/workManagement', async () => ({
  workRequest: vi.fn(),
  WorkRequestError: (await vi.importActual<typeof import('../../api/workManagement')>('../../api/workManagement')).WorkRequestError,
  boundedWorkRead: (read: (signal: AbortSignal) => Promise<unknown>) => read(new AbortController().signal),
}));
const id = '11111111-1111-4111-8111-111111111111';
const profile = (actor = id) => ({ id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en', timezone: 'UTC' });
const result = () => ({ items: [{ sourceKind: 'CARD', card: { id, organizationId: id, boardId: id, listId: id,
  title: 'Admitted Card', version: 1, lifecycleState: 'active', dueAt: null, dueComplete: false },
  boardName: 'Board', listName: 'List', labels: [], members: [], hasMoreLabels: false, hasMoreMembers: false }], nextCursor: 'opaque' });
beforeEach(() => { vi.mocked(workRequest).mockReset(); configureActivityTelemetry(false); });
afterEach(() => { cleanup(); configureActivityTelemetry(false); vi.unstubAllGlobals(); });
it('reports bounded use, failure, retry and reconnect observations without search criteria or results', async () => {
  configureActivityTelemetry(true);
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch);
  const request = vi.mocked(workRequest);
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile());
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  for (const [name, value] of [['Card text', 'private needle'], ['Label name', 'secret priority'], ['Member name', 'private teammate']])
    fireEvent.change(screen.getByRole('textbox', { name }), { target: { value } });
  fireEvent.click(screen.getByRole('button', { name: 'Search' })); await screen.findByRole('link', { name: 'Admitted Card' });
  request.mockRejectedValueOnce(new TypeError('private transport diagnostic'));
  fireEvent.click(screen.getByRole('button', { name: 'Refresh results' })); await screen.findByText(/Search is unavailable/);
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile());
  fireEvent(window, new Event('online')); await screen.findByRole('link', { name: 'Admitted Card' });
  await flushActivityTelemetry();
  const body = JSON.parse(fetch.mock.calls[0][1].body);
  expect(body.events).toEqual(expect.arrayContaining([
    { action: 'search_disclosure', kind: 'open', count: 1 }, { action: 'search_read', kind: 'use', count: 1 },
    { action: 'search_read', kind: 'retry', count: 1 }, { action: 'search_read', kind: 'reconnect', count: 1 },
    { action: 'search_read', kind: 'failure', count: 1, durationMs: expect.any(Number) },
    { action: 'search_read', kind: 'exception', count: 1 },
  ]));
  for (const value of ['private', 'secret', 'Admitted Card', 'opaque', id]) expect(JSON.stringify(body)).not.toContain(value);
});
it('submits criteria and replaces the bounded page while preserving its opaque continuation', async () => {
  const request = vi.mocked(workRequest); request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile());
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  fireEvent.change(screen.getByRole('textbox', { name: 'Card text' }), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search' }));
  expect(await screen.findByRole('link', { name: 'Admitted Card' })).toHaveAttribute('href', `/app/${id}/boards/${id}/cards/${id}`);
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce({ items: [], nextCursor: null }).mockResolvedValueOnce(profile());
  fireEvent.click(screen.getByRole('button', { name: 'Next search page' }));
  expect(await screen.findByText(/Search complete/)).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Admitted Card' })).not.toBeInTheDocument();
  expect(request.mock.calls.some(([path]) => typeof path === 'string' && path.includes('after=opaque'))).toBe(true);
});
it('withholds protected results and clears criteria when the account changes across the response', async () => {
  vi.mocked(workRequest).mockResolvedValueOnce(profile()).mockResolvedValueOnce(result())
    .mockResolvedValueOnce(profile('22222222-2222-4222-8222-222222222222'));
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  fireEvent.change(screen.getByRole('textbox', { name: 'Card text' }), { target: { value: 'private needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search' }));
  expect(await screen.findByText(/Search is unavailable/)).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Admitted Card' })).not.toBeInTheDocument();
  expect(screen.getByRole('textbox', { name: 'Card text' })).toHaveValue('');
});
it('recovers an admitted search after a transient offline read without manual reload', async () => {
  const request = vi.mocked(workRequest);
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile());
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  fireEvent.change(screen.getByRole('textbox', { name: 'Card text' }), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search' }));
  await screen.findByRole('link', { name: 'Admitted Card' });
  request.mockRejectedValueOnce(new TypeError('offline'));
  fireEvent.click(screen.getByRole('button', { name: 'Refresh results' }));
  await screen.findByText(/Search is unavailable/);
  expect(screen.queryByRole('link', { name: 'Admitted Card' })).not.toBeInTheDocument();
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile());
  fireEvent(window, new Event('online'));
  expect(await screen.findByRole('link', { name: 'Admitted Card' })).toBeInTheDocument();
  expect(screen.getByRole('textbox', { name: 'Card text' })).toHaveValue('needle');
});
it.each([401, 403, 404])('purges retained query state after terminal denial %s', async status => {
  const request = vi.mocked(workRequest);
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile());
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  fireEvent.change(screen.getByRole('textbox', { name: 'Card text' }), { target: { value: 'private needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search' }));
  await screen.findByRole('link', { name: 'Admitted Card' });
  request.mockRejectedValueOnce(new WorkRequestError(status, null));
  fireEvent.click(screen.getByRole('button', { name: 'Refresh results' }));
  await screen.findByText(/Search is unavailable/);
  expect(screen.queryByRole('link', { name: 'Admitted Card' })).not.toBeInTheDocument();
  expect(screen.getByRole('textbox', { name: 'Card text' })).toHaveValue('');
});
