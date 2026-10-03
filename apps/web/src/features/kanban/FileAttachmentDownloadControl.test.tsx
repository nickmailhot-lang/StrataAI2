import { act, fireEvent, render, screen } from '@testing-library/react';
import { workRequest } from '../../api/workManagement';
import { FileAttachmentDownloadControl } from './FileAttachmentDownloadControl';
import { parseAttachmentDownloadOptions, type FileAttachment } from './attachments';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const now = '2026-10-03T08:00:00.123456Z';
const file: FileAttachment = { id: id(4), organizationId: id(1), cardId: id(3), uploaderId: profile.id, kind: 0, displayName: 'Résumé.pdf',
  mimeType: 'application/pdf', sizeBytes: 100003, url: null, scanStatus: 2, scannedAt: now, createdAt: now, updatedAt: now, version: 2, deletedAt: null };
const options = { ...scope, cardVersion: 4, attachmentId: file.id, attachmentVersion: file.version, actorId: profile.id };
const props = { ...scope, version: 4, file, onRefresh: vi.fn() };
beforeEach(() => { vi.mocked(workRequest).mockReset(); props.onRefresh.mockReset(); });
afterEach(() => vi.useRealTimers());
function admit() { vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce(options).mockResolvedValueOnce(profile); }
async function check() { fireEvent.click(screen.getByRole('button', { name: 'Check file download access' })); }

it('reviews current actor and scope before exposing a native bounded-memory download with actor/version binding', async () => {
  admit(); render(<FileAttachmentDownloadControl {...props} />); expect(workRequest).not.toHaveBeenCalled(); expect(screen.queryByRole('link')).toBeNull();
  await check(); const link = await screen.findByRole('link', { name: 'Download Résumé.pdf (opens in a new tab)' });
  expect(link).toHaveAttribute('href', `/cards/${scope.cardId}/attachments/${file.id}/download?actorId=${profile.id}&attachmentVersion=2`);
  expect(link).toHaveAttribute('target', '_blank'); expect(link).toHaveAttribute('rel', 'noopener noreferrer'); expect(link).toHaveAttribute('referrerpolicy', 'no-referrer');
  expect(link).toHaveFocus(); expect(workRequest).toHaveBeenCalledTimes(3);
  expect(vi.mocked(workRequest).mock.calls.map(call => call[0])).toEqual(['/me', `/cards/${scope.cardId}/attachments/${file.id}/download-options`, '/me']);
  fireEvent.click(link); expect(screen.getByRole('status')).toHaveTextContent('Download requested. Your browser will report whether it completes.');
  expect(workRequest).toHaveBeenCalledTimes(3);
});

it.each(['organizationId', 'boardId', 'cardId', 'attachmentId', 'actorId', 'cardVersion', 'attachmentVersion', 'privateExtra'])('refuses changed or private options field %s', async field => {
  const bad = { ...options, [field]: field.endsWith('Version') ? 99 : id(99) };
  vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce(bad);
  render(<FileAttachmentDownloadControl {...props} />); await check();
  expect(await screen.findByRole('alert')).toHaveTextContent('File download is unavailable.'); expect(screen.queryByRole('link')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Refresh Card for file download' })); expect(props.onRefresh).toHaveBeenCalledOnce();
});

it('refuses a session change after a successful options read', async () => {
  vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce(options).mockResolvedValueOnce({ ...profile, id: id(9) });
  render(<FileAttachmentDownloadControl {...props} />); await check(); await screen.findByRole('alert'); expect(screen.queryByRole('link')).toBeNull();
});

it('expires a review and removes its native link without issuing another request', async () => {
  admit(); render(<FileAttachmentDownloadControl {...props} />); await check(); const link = await screen.findByRole('link');
  vi.useFakeTimers(); // The review already scheduled a real timer; reschedule with a fresh review under the fake clock.
  admit(); await check(); await act(async () => { await Promise.resolve(); await Promise.resolve(); });
  await act(async () => { vi.advanceTimersByTime(60000); });
  expect(screen.queryByRole('link')).toBeNull(); expect(screen.getByRole('status')).toHaveTextContent('Download review expired.');
  expect(link).not.toBeInTheDocument(); expect(workRequest).toHaveBeenCalledTimes(6);
});

it('stops a stalled review and ignores its late result', async () => {
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(value => { resolve = value; }));
  render(<FileAttachmentDownloadControl {...props} />); await check();
  fireEvent.click(screen.getByRole('button', { name: 'Stop download review' })); expect(screen.getByRole('status')).toHaveTextContent('Download review stopped.');
  await act(async () => { resolve(profile); }); expect(screen.queryByRole('link')).toBeNull(); expect(workRequest).toHaveBeenCalledOnce();
});

it('removes admitted links on Card revision changes and ignores old scope requests', async () => {
  admit(); const view = render(<FileAttachmentDownloadControl {...props} />); await check(); await screen.findByRole('link');
  view.rerender(<FileAttachmentDownloadControl {...props} version={5} />); expect(screen.queryByRole('link')).toBeNull();
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(value => { resolve = value; })); await check();
  view.rerender(<FileAttachmentDownloadControl {...props} cardId={id(9)} />); await act(async () => { resolve(profile); });
  expect(screen.queryByRole('link')).toBeNull(); expect(workRequest).toHaveBeenCalledTimes(4);
});

it('does not disclose a control for quarantined files or accept their options', () => {
  const pending: FileAttachment = { ...file, scanStatus: 1, scannedAt: null, version: 1 };
  render(<FileAttachmentDownloadControl {...props} file={pending} />); expect(screen.queryByRole('button')).toBeNull();
  expect(() => parseAttachmentDownloadOptions(options, scope, 4, pending, profile.id)).toThrow(); expect(workRequest).not.toHaveBeenCalled();
});
