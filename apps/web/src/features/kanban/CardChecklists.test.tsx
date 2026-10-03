import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardChecklists } from './CardChecklists';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const rank = (n: number) => String(n).padStart(30, '0');
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const now = '2026-10-03T01:00:00.123456Z';
const checklist = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, title: 'Preparations', rank: rank(1),
  createdAt: now, updatedAt: now, version: 2, deletedAt: null };
const summary = { checklist, completed: 1, total: 2, percent: 50 };
const page = { ...scope, cardVersion: 4, canEdit: false, items: [summary], nextCursor: null };
const items = { ...scope, cardVersion: 4, canEdit: false, summary, items: [
  { id: id(5), organizationId: scope.organizationId, checklistId: checklist.id, text: 'Completed preparation', rank: rank(1),
    completed: true, completedAt: now, completedBy: id(8), createdAt: now, updatedAt: now, version: 2, deletedAt: null },
  { id: id(6), organizationId: scope.organizationId, checklistId: checklist.id, text: 'Pending preparation', rank: rank(2),
    completed: false, completedAt: null, completedBy: null, createdAt: now, updatedAt: now, version: 1, deletedAt: null },
], nextCursor: null };
const props = { ...scope, version: 4, unavailable: false, onRefresh: vi.fn() };
const respond = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } });
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });
it('preserves expanded item intent across revision/access refresh but removes old content before reading current state', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(respond(page)).mockResolvedValueOnce(respond(items))
    .mockResolvedValueOnce(respond({ ...page, cardVersion: 5 }))
    .mockResolvedValueOnce(respond({ ...items, cardVersion: 5, items: [{ ...items.items[1], text: 'Current preparation' }], summary: { ...summary, completed: 0, total: 1, percent: 0 } }));
  vi.stubGlobal('fetch', fetch); const view = render(<CardChecklists {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Show items in Preparations' })); await screen.findByText('Incomplete: Pending preparation');
  view.rerender(<CardChecklists {...props} version={5} unavailable />);
  expect(screen.queryByText('Incomplete: Pending preparation')).not.toBeInTheDocument(); expect(screen.queryByRole('heading')).not.toBeInTheDocument();
  expect(fetch).toHaveBeenCalledTimes(2); view.rerender(<CardChecklists {...props} version={5} />);
  expect(await screen.findByText('Incomplete: Current preparation')).toBeVisible(); expect(screen.getByRole('button', { name: 'Hide items in Preparations' })).toHaveAttribute('aria-expanded', 'true');
  expect(screen.queryByText('Incomplete: Pending preparation')).not.toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(4);
});
it('accepts generic disclosure intent during re-admission without reading protected content', async () => {
  const fetch = vi.fn().mockResolvedValue(respond(page)); vi.stubGlobal('fetch', fetch);
  const view = render(<CardChecklists {...props} unavailable />); const trigger = screen.getByRole('button', { name: 'Show checklists' }); trigger.focus(); fireEvent.click(trigger);
  expect(screen.getByText('Checking current Card access…')).toBeVisible(); expect(fetch).not.toHaveBeenCalled();
  view.rerender(<CardChecklists {...props} />); expect(await screen.findByRole('heading', { name: 'Preparations' })).toBeVisible();
  expect(screen.getByRole('button', { name: 'Hide checklists' })).toBe(trigger); expect(trigger).toHaveFocus();
});

it('loads lazily, shows authoritative progress and offers named read-only item disclosure', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(respond(page)).mockResolvedValueOnce(respond(items)); vi.stubGlobal('fetch', fetch);
  render(<CardChecklists {...props} />); expect(fetch).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  expect(await screen.findByText('1 of 2 items complete (50%)')).toBeVisible();
  expect(screen.getByText('Read-only checklists.')).toBeVisible();
  expect(screen.getByRole('progressbar', { name: 'Progress for Preparations' })).toHaveAttribute('aria-valuenow', '50');
  fireEvent.click(screen.getByRole('button', { name: 'Show items in Preparations' }));
  expect(await screen.findByText('Complete: Completed preparation')).toBeVisible();
  expect(screen.getByText('Incomplete: Pending preparation')).toBeVisible();
  expect(fetch.mock.calls.map(call => call[0])).toEqual([`/cards/${scope.cardId}/checklists`, `/cards/${scope.cardId}/checklists/${checklist.id}/items`]);
  fireEvent.click(screen.getByRole('button', { name: 'Hide checklists' }));
  expect(screen.queryByText('Pending preparation')).not.toBeInTheDocument();
});
it('never renders foreign content and retries through the same safe read boundary', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(respond({ ...page, boardId: id(90), items: [{ ...summary, checklist: { ...checklist, title: 'Protected foreign title' } }] }))
    .mockResolvedValueOnce(respond(page)); vi.stubGlobal('fetch', fetch);
  render(<CardChecklists {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load current checklists');
  expect(screen.queryByText('Protected foreign title')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry checklist read' }));
  expect(await screen.findByRole('heading', { name: 'Preparations' })).toBeVisible(); expect(fetch).toHaveBeenCalledTimes(2);
});
it('cancels in-flight reads and clears contents during access re-admission', async () => {
  let resolve: (value: Response) => void = () => {}; let signal: AbortSignal | undefined;
  const fetch = vi.fn().mockImplementationOnce((_path: string, options: RequestInit) => { signal = options.signal as AbortSignal; return new Promise<Response>(yes => { resolve = yes; }); })
    .mockResolvedValueOnce(respond(page)); vi.stubGlobal('fetch', fetch);
  const view = render(<CardChecklists {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  view.rerender(<CardChecklists {...props} unavailable />); expect(signal?.aborted).toBe(true);
  await act(async () => resolve(respond(page)));
  expect(screen.queryByRole('heading', { name: 'Preparations' })).not.toBeInTheDocument();
  expect(screen.getByText('Checking current Card access…')).toBeVisible();
  view.rerender(<CardChecklists {...props} />);
  expect(await screen.findByRole('heading', { name: 'Preparations' })).toBeVisible();
  view.rerender(<CardChecklists {...props} unavailable />);
  expect(screen.queryByRole('heading', { name: 'Preparations' })).not.toBeInTheDocument();
});
it('rejects a revision mismatch and does not display server error content', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(respond({ ...page, cardVersion: 5 }))
    .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Secret SQL details', code: 'card_not_found' }), { status: 404 })); vi.stubGlobal('fetch', fetch);
  render(<CardChecklists {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load current checklists');
  expect(screen.queryByRole('heading', { name: 'Preparations' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry checklist read' }));
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('checklists are unavailable'));
  expect(screen.queryByText('Secret SQL details')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Refresh Board' })); expect(props.onRefresh).toHaveBeenCalledOnce();
});
it('uses bounded seek pagination and preserves full progress on later item pages', async () => {
  const first = { ...items, summary: { ...summary, completed: 0, total: 51, percent: 0 }, items: Array.from({ length: 50 }, (_, n) => ({ ...items.items[1], id: id(n + 20), rank: rank(n + 1), text: `Task ${n + 1}` })),
    nextCursor: `${checklist.id}/${rank(50)}/${id(69)}` };
  const second = { ...first, items: [{ ...items.items[1], id: id(70), rank: rank(51), text: 'Final task' }], nextCursor: null };
  const fetch = vi.fn().mockResolvedValueOnce(respond({ ...page, items: [first.summary] })).mockResolvedValueOnce(respond(first)).mockResolvedValueOnce(respond(second)); vi.stubGlobal('fetch', fetch);
  render(<CardChecklists {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Show items in Preparations' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Next items in Preparations' }));
  expect(await screen.findByText('Incomplete: Final task')).toBeVisible();
  expect(screen.getByText('0 of 51 items complete (0%)')).toBeVisible();
  expect(fetch.mock.calls[2][0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}/items?after=${encodeURIComponent(first.nextCursor)}`);
  expect(screen.getByRole('button', { name: 'First items in Preparations' })).toBeVisible();
});
