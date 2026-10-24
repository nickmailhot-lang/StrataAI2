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
const id = '11111111-1111-4111-8111-111111111111';
const profile = { id, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en', timezone: 'UTC' };
const result = (nextCursor: string | null = 'next') => ({ items: [], nextCursor, interaction: { eventId: id, entityId: id, actorId: id,
  eventType: 'SEARCH_EXECUTED', entityType: 'Search', version: 1, organizationId: null, boardId: null,
  metadata: {}, createdAt: '2026-10-23T12:00:00Z' } });
const hint = 'Filter changes are not applied. Select Search to update results. Paging and Refresh results still use the last search.';
const request = vi.mocked(workRequest);
beforeEach(() => request.mockReset());
afterEach(cleanup);
function queue(nextCursor: string | null = 'next') { request.mockResolvedValueOnce(profile).mockResolvedValueOnce(result(nextCursor)).mockResolvedValueOnce(profile); }
async function admit() {
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  const query = screen.getByRole('textbox', { name: 'Card text' });
  fireEvent.change(query, { target: { value: 'landscaping' } }); queue(); fireEvent.submit(query.closest('form')!);
  await screen.findByRole('button', { name: 'Next search page' }); return query;
}
it('shows no result hint or request before the first admitted search', () => {
  render(<MemoryRouter><GlobalSearchPage /></MemoryRouter>);
  fireEvent.change(screen.getByRole('textbox', { name: 'Card text' }), { target: { value: 'draft' } });
  expect(screen.queryByText(hint)).not.toBeInTheDocument(); expect(request).not.toHaveBeenCalled();
});
it.each(['Card text', 'Label name', 'Member name'])('identifies an unapplied %s edit without submitting or moving focus', async name => {
  await admit(); const input = screen.getByRole('textbox', { name }); act(() => input.focus());
  const before = request.mock.calls.length; fireEvent.change(input, { target: { value: 'unsent' } });
  expect(screen.getByText(hint)).toBeVisible(); expect(input).toHaveFocus(); expect(request).toHaveBeenCalledTimes(before);
});
it.each([['Match criteria', 'Match any'], ['Search scope', 'Archived work']])('identifies an unapplied %s selection', async (name, value) => {
  await admit(); const before = request.mock.calls.length;
  fireEvent.mouseDown(screen.getByRole('combobox', { name })); fireEvent.click(await screen.findByRole('option', { name: value }));
  expect(screen.getByText(hint)).toBeVisible(); expect(request).toHaveBeenCalledTimes(before);
});
it('compares normalized submitted values and removes the hint after reverting the draft', async () => {
  const query = await admit(); fireEvent.change(query, { target: { value: '  landscaping  ' } });
  expect(screen.queryByText(hint)).not.toBeInTheDocument();
  fireEvent.change(query, { target: { value: 'roof' } }); expect(screen.getByText(hint)).toBeVisible();
  fireEvent.change(query, { target: { value: 'landscaping' } }); expect(screen.queryByText(hint)).not.toBeInTheDocument();
});
it('keeps applied criteria for paging and background refresh while preserving the edited field and focus', async () => {
  const query = await admit(); fireEvent.change(query, { target: { value: 'roof' } });
  queue(null); fireEvent.click(screen.getByRole('button', { name: 'Next search page' }));
  await screen.findByText(hint); expect(String(request.mock.calls.find(([path]) => String(path).includes('after=next'))![0])).toContain('q=landscaping');
  act(() => query.focus()); queue(null); fireEvent(window, new Event('online'));
  await screen.findByText(hint); expect(query).toHaveFocus(); expect(query).toHaveValue('roof');
  expect(request.mock.calls.filter(([path]) => String(path).startsWith('/search?')).every(([path]) => String(path).includes('q=landscaping'))).toBe(true);
});
it('hides stale guidance during submission and clears it only after the new canonical page is admitted', async () => {
  const query = await admit(); fireEvent.change(query, { target: { value: 'roof' } });
  let resolve!: (value: unknown) => void;
  request.mockResolvedValueOnce(profile).mockImplementationOnce(() => new Promise(r => { resolve = r; })).mockResolvedValueOnce(profile);
  act(() => query.focus()); fireEvent.submit(query.closest('form')!); await waitFor(() => expect(resolve).toBeTypeOf('function'));
  expect(screen.queryByText(hint)).not.toBeInTheDocument(); expect(screen.getByText('Searching…')).toBeVisible();
  await act(async () => resolve(result())); await screen.findByRole('button', { name: 'Next search page' });
  expect(query).toHaveFocus(); expect(screen.queryByText(hint)).not.toBeInTheDocument();
  expect(String(request.mock.calls.filter(([path]) => String(path).startsWith('/search?')).at(-1)![0])).toContain('q=roof');
});
it.each([503, 401, 403, 404])('withdraws guidance on %s without weakening draft privacy rules', async status => {
  const query = await admit(); fireEvent.change(query, { target: { value: 'private draft' } });
  request.mockRejectedValueOnce(new WorkRequestError(status, null)); fireEvent.click(screen.getByRole('button', { name: 'Refresh results' }));
  await screen.findByText(/Search is unavailable/); expect(screen.queryByText(hint)).not.toBeInTheDocument();
  expect(query).toHaveValue(status === 503 ? 'private draft' : '');
});
