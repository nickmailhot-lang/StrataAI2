import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { workRequest, WorkRequestError } from '../../api/workManagement';
import { GlobalSearchPage } from './GlobalSearchPage';
vi.mock('../../api/workManagement', async () => ({
  workRequest: vi.fn(),
  WorkRequestError: (await vi.importActual<typeof import('../../api/workManagement')>('../../api/workManagement')).WorkRequestError,
  boundedWorkRead: (read: (signal: AbortSignal) => Promise<unknown>) => read(new AbortController().signal),
}));
const actor = '11111111-1111-4111-8111-111111111111';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en', timezone: 'UTC' };
const result = (nextCursor: string | null = null) => ({ items: [], nextCursor,
  interaction: { eventId: actor, entityId: actor, actorId: actor, eventType: 'SEARCH_EXECUTED', entityType: 'Search',
    version: 1, organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-05T12:00:00Z' } });
const request = vi.mocked(workRequest);
beforeEach(() => request.mockReset());
afterEach(cleanup);
async function admit() {
  request.mockResolvedValueOnce(profile).mockResolvedValueOnce(result('next')).mockResolvedValueOnce(profile);
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  const query = screen.getByRole('textbox', { name: 'Card text' });
  fireEvent.change(query, { target: { value: 'retained draft' } });
  act(() => query.focus()); fireEvent.submit(query.closest('form')!);
  await screen.findByRole('button', { name: 'Next search page' });
  expect(query).toHaveFocus();
  return query;
}
it('keeps keyboard focus on stable search status while Next retires, through canonical completion', async () => {
  const query = await admit(); let resolve!: (value: unknown) => void;
  request.mockResolvedValueOnce(profile).mockImplementationOnce(() => new Promise(r => { resolve = r; })).mockResolvedValueOnce(profile);
  const next = screen.getByRole('button', { name: 'Next search page' }); act(() => next.focus()); fireEvent.click(next);
  await waitFor(() => expect(resolve).toBeTypeOf('function'));
  expect(screen.getByRole('status')).toHaveFocus();
  expect(screen.getByText('Searching…')).toBeInTheDocument();
  await act(async () => resolve(result()));
  await screen.findByText(/Search complete/);
  expect(screen.getByRole('status')).toHaveFocus();
  expect(query).toHaveValue('retained draft');
  expect(request.mock.calls.filter(([path]) => String(path).startsWith('/search?')).map(([path]) => String(path)))
    .toEqual([expect.not.stringContaining('after='), expect.stringContaining('after=next')]);
});
it.each([503, 403])('keeps a retired Refresh control attached to safe feedback after %s', async status => {
  const query = await admit();
  request.mockRejectedValueOnce(new WorkRequestError(status, null));
  const refresh = screen.getByRole('button', { name: 'Refresh results' }); act(() => refresh.focus()); fireEvent.click(refresh);
  await screen.findByText(/Search is unavailable/);
  expect(screen.getByRole('status')).toHaveFocus();
  expect(query).toHaveValue(status === 403 ? '' : 'retained draft');
  expect(screen.queryByRole('region', { name: 'Search guidance' })).not.toBeInTheDocument();
});
it('does not steal focus from an edited filter when background reconciliation completes', async () => {
  const query = await admit();
  fireEvent.change(query, { target: { value: 'unsent edit' } }); act(() => query.focus());
  request.mockResolvedValueOnce(profile).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile);
  fireEvent(window, new Event('online'));
  await screen.findByText(/Search complete/);
  expect(query).toHaveFocus(); expect(query).toHaveValue('unsent edit');
  expect(String(request.mock.calls.filter(([path]) => String(path).startsWith('/search?')).at(-1)![0])).toContain('q=retained+draft');
});
it('moves only a retiring paging control to status during background reconciliation', async () => {
  await admit(); const next = screen.getByRole('button', { name: 'Next search page' }); act(() => next.focus());
  request.mockResolvedValueOnce(profile).mockResolvedValueOnce(result()).mockResolvedValueOnce(profile);
  fireEvent(window, new Event('online'));
  await screen.findByText(/Search complete/);
  expect(screen.getByRole('status')).toHaveFocus();
});
it('does not reclaim focus after the user resumes editing during a pending page read', async () => {
  const query = await admit(); let resolve!: (value: unknown) => void;
  request.mockResolvedValueOnce(profile).mockImplementationOnce(() => new Promise(r => { resolve = r; })).mockResolvedValueOnce(profile);
  const next = screen.getByRole('button', { name: 'Next search page' }); act(() => next.focus()); fireEvent.click(next);
  await waitFor(() => expect(resolve).toBeTypeOf('function'));
  expect(screen.getByRole('status')).toHaveFocus();
  act(() => query.focus()); fireEvent.change(query, { target: { value: 'new unsent edit' } });
  await act(async () => resolve(result()));
  await screen.findByText(/Search complete/);
  expect(query).toHaveFocus(); expect(query).toHaveValue('new unsent edit');
});
