import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardAttachments } from './CardAttachments';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const now = '2026-10-03T08:00:00.123456Z';
const item = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, uploaderId: id(8), kind: 1, displayName: 'External reference',
  mimeType: null, sizeBytes: null, url: 'https://example.test/reference', scanStatus: 0, scannedAt: null, createdAt: now, updatedAt: now, version: 1, deletedAt: null };
const page = { ...scope, cardVersion: 4, canEdit: false, items: [item], nextCursor: null };
const props = { ...scope, version: 4, unavailable: false, onRefresh: vi.fn() };
const respond = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } });
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

it('loads only on explicit disclosure and renders a safe external link without fetching its target', async () => {
  const fetch = vi.fn().mockResolvedValue(respond(page)); vi.stubGlobal('fetch', fetch);
  render(<CardAttachments {...props} />); expect(fetch).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Show attachments' }));
  const link = await screen.findByRole('link', { name: 'External reference (opens in a new tab)' });
  expect(link).toHaveAttribute('href', item.url); expect(link).toHaveAttribute('target', '_blank');
  expect(link).toHaveAttribute('rel', 'noopener noreferrer'); expect(link).toHaveAttribute('referrerpolicy', 'no-referrer');
  expect(screen.getByText('Read-only attachments.')).toBeVisible();
  expect(fetch).toHaveBeenCalledOnce(); expect(fetch.mock.calls[0][0]).toContain(`/cards/${scope.cardId}/attachments`);
  expect(screen.getByRole('button', { name: 'Hide attachments' })).toHaveAttribute('aria-expanded', 'true');
  fireEvent.click(screen.getByRole('button', { name: 'Hide attachments' })); expect(screen.queryByRole('link')).not.toBeInTheDocument();
});
it('retains disclosure intent through re-admission but aborts and hides late protected content', async () => {
  let resolve: (value: Response) => void = () => {}; let signal: AbortSignal | undefined;
  const fetch = vi.fn().mockImplementationOnce((_path: string, options: RequestInit) => { signal = options.signal as AbortSignal; return new Promise<Response>(yes => { resolve = yes; }); })
    .mockResolvedValueOnce(respond(page)); vi.stubGlobal('fetch', fetch);
  const view = render(<CardAttachments {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show attachments' }));
  await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
  view.rerender(<CardAttachments {...props} unavailable />); expect(signal?.aborted).toBe(true);
  await act(async () => resolve(respond(page))); expect(screen.queryByRole('link')).not.toBeInTheDocument();
  expect(screen.getByText('Checking current Card access…')).toBeVisible();
  view.rerender(<CardAttachments {...props} />); await screen.findByRole('link');
  view.rerender(<CardAttachments {...props} unavailable />); expect(screen.queryByRole('link')).not.toBeInTheDocument();
});
it('preserves a Show click while access is checked without issuing a protected read until admission finishes', async () => {
  const fetch = vi.fn().mockResolvedValue(respond(page)); vi.stubGlobal('fetch', fetch);
  const view = render(<CardAttachments {...props} unavailable />); fireEvent.click(screen.getByRole('button', { name: 'Show attachments' }));
  expect(fetch).not.toHaveBeenCalled(); view.rerender(<CardAttachments {...props} />); await screen.findByRole('link');
  expect(fetch).toHaveBeenCalledOnce();
});
it.each([{ ...page, boardId: id(90) }, { ...page, cardVersion: 5 }, { ...page, items: [{ ...item, url: 'javascript:alert(1)' }] },
  { ...page, items: [{ ...item, storageKey: 'private/key' }] }])('rejects foreign, stale, unsafe or private-field responses without exposing content (%j)', async value => {
  const fetch = vi.fn().mockResolvedValueOnce(respond(value)).mockResolvedValueOnce(respond(page)); vi.stubGlobal('fetch', fetch);
  render(<CardAttachments {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show attachments' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load current attachments'); expect(screen.queryByRole('link')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry attachments' })); await screen.findByRole('link'); expect(fetch).toHaveBeenCalledTimes(2);
});
it('clears the old revision before current read and resets disclosure completely on Card scope change', async () => {
  let resolve: (value: Response) => void = () => {};
  const fetch = vi.fn().mockResolvedValueOnce(respond(page)).mockImplementationOnce(() => new Promise<Response>(yes => { resolve = yes; })); vi.stubGlobal('fetch', fetch);
  const view = render(<CardAttachments {...props} />); fireEvent.click(screen.getByRole('button', { name: 'Show attachments' })); await screen.findByRole('link');
  view.rerender(<CardAttachments {...props} version={5} />); expect(screen.queryByRole('link')).not.toBeInTheDocument();
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2)); view.rerender(<CardAttachments {...props} cardId={id(99)} version={5} />);
  await act(async () => resolve(respond({ ...page, cardVersion: 5 }))); expect(screen.queryByRole('link')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Show attachments' })).toHaveAttribute('aria-expanded', 'false'); expect(fetch).toHaveBeenCalledTimes(2);
});
