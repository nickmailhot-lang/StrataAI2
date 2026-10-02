import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardLabels } from './CardLabels';

const org = '11111111-1111-1111-1111-111111111111', board = '22222222-2222-2222-2222-222222222222', card = '33333333-3333-3333-3333-333333333333';
const props = { organizationId: org, boardId: board, cardId: card, version: 2, unavailable: false, onRefresh: vi.fn() };
const label = (i = 1) => ({ id: `44444444-4444-4444-4444-${String(i).padStart(12, '0')}`, organizationId: org, boardId: board, name: i === 1 ? 'Important' : '', color: 'blue', rank: '500000000000000000000000000000', deleted: false, version: 1 });
const page = (items = [label()]) => ({ organizationId: org, boardId: board, cardId: card, cardVersion: 2, canEdit: true, items, nextCursor: null as string | null });
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
it('loads on demand and exposes readable names and color descriptions', async () => {
  const fetch = vi.fn().mockResolvedValue(response(page([label(), label(2)]))); vi.stubGlobal('fetch', fetch);
  render(<CardLabels {...props} />); expect(fetch).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Show labels' }));
  expect(await screen.findByLabelText('Important, blue')).toBeVisible(); expect(screen.getByText('blue label')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Hide labels' })).toHaveAttribute('aria-expanded', 'true');
});
it('appends a bounded later page and restarts cleanly after hiding', async () => {
  const items = Array.from({ length: 50 }, (_, i) => label(i + 1));
  const fetch = vi.fn().mockResolvedValueOnce(response({ ...page(items), nextCursor: items[49].id }))
    .mockResolvedValueOnce(response(page([label(51)]))).mockResolvedValueOnce(response(page())); vi.stubGlobal('fetch', fetch);
  render(<CardLabels {...props} />); fireEvent.click(screen.getByText('Show labels'));
  fireEvent.click(await screen.findByText('Load more labels'));
  await waitFor(() => expect(screen.queryByText('Load more labels')).not.toBeInTheDocument());
  expect(screen.getAllByLabelText('Unnamed label, blue')).toHaveLength(50);
  expect(fetch.mock.calls[1][0]).toContain(`after=${items[49].id}`);
  fireEvent.click(screen.getByText('Hide labels')); fireEvent.click(screen.getByText('Show labels'));
  await screen.findByLabelText('Important, blue'); expect(fetch.mock.calls[2][0]).not.toContain('after=');
});
it.each([401, 403, 404])('clears prior labels on a denied later page (%s)', async status => {
  const items = Array.from({ length: 50 }, (_, i) => label(i + 1));
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(response({ ...page(items), nextCursor: items[49].id })).mockResolvedValueOnce(response({ detail: 'Private server text' }, status)));
  render(<CardLabels {...props} />); fireEvent.click(screen.getByText('Show labels')); fireEvent.click(await screen.findByText('Load more labels'));
  expect(await screen.findByRole('alert')).toHaveTextContent('Labels are unavailable');
  expect(screen.queryByText('Important')).not.toBeInTheDocument(); expect(screen.queryByText('Private server text')).not.toBeInTheDocument();
});
it.each(['scope', 'version', 'duplicate', 'palette'])('rejects malformed %s without rendering label text', async invalid => {
  const value = page();
  if (invalid === 'scope') value.boardId = org;
  if (invalid === 'version') value.cardVersion = 3;
  if (invalid === 'duplicate') value.items.push(label());
  if (invalid === 'palette') value.items[0].color = 'unknown';
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(value)));
  render(<CardLabels {...props} />); fireEvent.click(screen.getByText('Show labels'));
  await screen.findByRole('alert'); expect(screen.queryByText('Important')).not.toBeInTheDocument();
});
it('discards a late response after the Card revision changes', async () => {
  let finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn().mockReturnValue(new Promise<Response>(resolve => { finish = resolve; })));
  const view = render(<CardLabels {...props} />); fireEvent.click(screen.getByText('Show labels'));
  await waitFor(() => expect(finish).toBeDefined()); view.rerender(<CardLabels {...props} version={3} />);
  await act(async () => { finish(response(page())); });
  expect(screen.queryByText('Important')).not.toBeInTheDocument(); expect(screen.getByText('Show labels')).toBeVisible();
});
