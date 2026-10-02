import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardAssignees } from './CardAssignees';
const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222', card = '33333333-3333-3333-3333-333333333333';
const props = { organizationId: org, boardId: board, cardId: card, version: 2, unavailable: false, onRefresh: vi.fn() };
const member = (i = 1) => ({ userId: `44444444-4444-4444-4444-${String(i).padStart(12, '0')}`, displayName: `Member ${i}`, assignedBy: org, assignedAt: '2026-10-02T12:00:00Z' });
const page = (items = [member()], nextCursor: string | null = null) => ({ organizationId: org, boardId: board, cardId: card, cardVersion: 2, canEdit: true, items, nextCursor });
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => vi.unstubAllGlobals());
it('reads on demand and exposes readable member names with initials', async () => {
  const fetch = vi.fn().mockResolvedValue(response(page())); vi.stubGlobal('fetch', fetch); render(<CardAssignees {...props} />);
  expect(fetch).not.toHaveBeenCalled(); fireEvent.click(screen.getByRole('button', { name: 'Show assignees' }));
  expect(await screen.findByText('Member 1')).toBeVisible(); expect(screen.getByText('M1')).toHaveAttribute('aria-hidden', 'true');
  expect(screen.getByRole('region', { name: 'Card assignees' })).toBeVisible();
});
it('replaces bounded pages and restarts after hiding', async () => {
  const items = Array.from({ length: 50 }, (_, i) => member(i + 1));
  const fetch = vi.fn().mockResolvedValueOnce(response(page(items, items[49].userId))).mockResolvedValueOnce(response(page([member(51)]))).mockResolvedValueOnce(response(page()));
  vi.stubGlobal('fetch', fetch); render(<CardAssignees {...props} />); fireEvent.click(screen.getByText('Show assignees'));
  fireEvent.click(await screen.findByText('Next assignees')); await screen.findByText('Member 51'); expect(screen.queryByText('Member 1')).not.toBeInTheDocument();
  expect(fetch.mock.calls[1][0]).toContain(`after=${items[49].userId}`);
  fireEvent.click(screen.getByText('Hide assignees')); fireEvent.click(screen.getByText('Show assignees')); await screen.findByText('Member 1');
  expect(fetch.mock.calls[2][0]).not.toContain('after=');
});
it.each([401, 403, 404])('clears names and gives safe recovery after denial %s', async status => {
  const items = Array.from({ length: 50 }, (_, i) => member(i + 1));
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response(page(items, items[49].userId))).mockResolvedValueOnce(response({ detail: 'Private server text' }, status)));
  render(<CardAssignees {...props} />); fireEvent.click(screen.getByText('Show assignees')); fireEvent.click(await screen.findByText('Next assignees'));
  await screen.findByRole('alert'); expect(screen.queryByText('Member 1')).not.toBeInTheDocument(); expect(screen.queryByText('Private server text')).not.toBeInTheDocument();
});
it.each(['scope', 'revision', 'duplicate', 'attribution', 'timestamp', 'cursor'])('rejects malformed %s pages before rendering names', async invalid => {
  const p = page(); if (invalid === 'scope') p.boardId = org; if (invalid === 'revision') p.cardVersion = 3;
  if (invalid === 'duplicate') p.items.push(member()); if (invalid === 'attribution') p.items[0].assignedBy = 'invalid';
  if (invalid === 'timestamp') p.items[0].assignedAt = 'invalid'; if (invalid === 'cursor') p.nextCursor = member().userId;
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(p))); render(<CardAssignees {...props} />); fireEvent.click(screen.getByText('Show assignees'));
  await screen.findByRole('alert'); expect(screen.queryByText('Member 1')).not.toBeInTheDocument();
});
it('fences late results when canonical revision or access changes', async () => {
  let finish!: (r: Response) => void; vi.stubGlobal('fetch', vi.fn().mockReturnValue(new Promise<Response>(resolve => { finish = resolve; })));
  const view = render(<CardAssignees {...props} />); fireEvent.click(screen.getByText('Show assignees')); await waitFor(() => expect(finish).toBeDefined());
  view.rerender(<CardAssignees {...props} version={3} unavailable />); await act(async () => finish(response(page())));
  expect(screen.queryByText('Member 1')).not.toBeInTheDocument(); expect(screen.getByText('Hide assignees')).toBeDisabled();
});
it('keeps disclosure intent through access/revision refresh while discarding old names and reading the new revision', async () => {
  let finish!: (value: Response) => void; const stale = new Promise<Response>(resolve => { finish = resolve; });
  const fetch = vi.fn().mockReturnValueOnce(stale).mockResolvedValueOnce(response({ ...page([member(2)]), cardVersion: 3 }));
  vi.stubGlobal('fetch', fetch); const view = render(<CardAssignees {...props} />); fireEvent.click(screen.getByText('Show assignees'));
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1)); view.rerender(<CardAssignees {...props} version={3} unavailable />);
  expect(screen.getByText('Hide assignees')).toBeDisabled(); expect(screen.queryByText('Member 1')).not.toBeInTheDocument();
  view.rerender(<CardAssignees {...props} version={3} />); expect(await screen.findByText('Member 2')).toBeVisible();
  await act(async () => finish(response(page())));
  expect(screen.queryByText('Member 1')).not.toBeInTheDocument(); expect(screen.getByText('Member 2')).toBeVisible();
  expect(screen.getByText('Hide assignees')).toHaveAttribute('aria-expanded', 'true'); expect(fetch).toHaveBeenCalledTimes(2);
});
