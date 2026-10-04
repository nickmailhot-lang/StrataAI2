import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { workRequest } from '../../api/workManagement';
import { GlobalSearchPage } from './GlobalSearchPage';
vi.mock('../../api/workManagement', () => ({
  workRequest: vi.fn(),
  boundedWorkRead: (read: (signal: AbortSignal) => Promise<unknown>) => read(new AbortController().signal),
}));
const id = '11111111-1111-4111-8111-111111111111';
const profile = (actor = id) => ({ id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en', timezone: 'UTC' });
const result = () => ({ items: [{ sourceKind: 'CARD', card: { id, organizationId: id, boardId: id, listId: id,
  title: 'Admitted Card', version: 1, lifecycleState: 'active', dueAt: null, dueComplete: false },
  boardName: 'Board', listName: 'List', labels: [], members: [], hasMoreLabels: false, hasMoreMembers: false }], nextCursor: 'opaque' });
beforeEach(() => vi.mocked(workRequest).mockReset());
afterEach(cleanup);
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
