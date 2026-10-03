import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { workRequest } from '../../api/workManagement';
import { FileAttachmentDownloadControl } from './FileAttachmentDownloadControl';
import { FileAttachmentPreviewControl } from './FileAttachmentPreviewControl';
import { AttachmentManageControl } from './AttachmentManageControl';
import { parseAttachmentDownloadOptions, parseArchivedAttachmentDownloadOptions, type ArchivedFileAttachment, type FileAttachment } from './attachments';

vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const now = '2026-10-03T08:00:00.123456Z'; const archivedAt = '2026-10-03T08:01:00.123456Z';
const file: ArchivedFileAttachment = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, uploaderId: profile.id,
  kind: 0, displayName: 'Retained.webp', mimeType: 'image/webp', sizeBytes: 1000, url: null,
  scanStatus: 2, scannedAt: now, createdAt: now, updatedAt: archivedAt, version: 4,
  lifecycleState: 1, archivedAt, deletedAt: null, deletedBy: null };
const options = { ...scope, cardVersion: 5, attachmentId: file.id, attachmentVersion: 4, actorId: profile.id };
const props = { ...scope, version: 5, file, archiveReview: true as const, onRefresh: vi.fn() };
const base = `/cards/${scope.cardId}/attachments/archive/${file.id}`;
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
function admit() { vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce(options).mockResolvedValueOnce(profile); }

it('uses separate protected archive download admission with current actor/revision binding', async () => {
  admit(); render(<FileAttachmentDownloadControl {...props} />); expect(workRequest).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Check archived file download access' }));
  const link = await screen.findByRole('link', { name: 'Download archived Retained.webp (opens in a new tab)' });
  expect(link).toHaveAttribute('href', `${base}/download?actorId=${profile.id}&attachmentVersion=4`);
  expect(link).toHaveAttribute('rel', 'noopener noreferrer'); expect(link).toHaveAttribute('referrerpolicy', 'no-referrer');
  expect(vi.mocked(workRequest).mock.calls.map(call => call[0])).toEqual(['/me', `${base}/download-options`, '/me']);
});
it('requests an archived sanitized preview only after explicit admission and retires it on revision change', async () => {
  admit(); const view = render(<FileAttachmentPreviewControl {...props} />); expect(screen.queryByRole('img')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Show archived image preview' }));
  const image = await screen.findByRole('img'); expect(image).toHaveAttribute('src', `${base}/preview?actorId=${profile.id}&attachmentVersion=4`);
  expect(vi.mocked(workRequest).mock.calls.map(call => call[0])).toEqual(['/me', `${base}/preview-options`, '/me']);
  view.rerender(<FileAttachmentPreviewControl {...props} version={6} />); expect(screen.queryByRole('img')).toBeNull();
});
it.each([{ lifecycleState: 0 }, { archivedAt: null }, { deletedAt: archivedAt }, { storageKey: 'private/key' }])('refuses malformed archive source %j and ordinary admission of archived metadata', patch => {
  expect(() => parseArchivedAttachmentDownloadOptions(options, scope, 5, { ...file, ...patch } as ArchivedFileAttachment, profile.id)).toThrow();
  expect(() => parseAttachmentDownloadOptions(options, scope, 5, file as unknown as FileAttachment, profile.id)).toThrow();
});
it('offers protected review to a read-only member and removes delivery controls after a changed Card snapshot', async () => {
  const archivePage = { ...scope, cardVersion: 5, items: [file], nextCursor: null, canRestore: false, canDelete: false };
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : path.endsWith('/archive') ? archivePage
    : path.endsWith('-options') ? options : { ...scope, cardVersion: 5, items: [], nextCursor: null, canEdit: false });
  const p = { ...scope, version: 5, editable: false, canAdminister: false, disabled: false, unavailable: false,
    onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() };
  const view = render(<AttachmentManageControl {...p} />);
  fireEvent.click(screen.getByRole('button', { name: 'Manage attachments' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Review attachment archive' }));
  const download = await screen.findByRole('button', { name: 'Check archived file download access' });
  expect(screen.getByRole('button', { name: 'Restore attachment Retained.webp' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Delete attachment Retained.webp' })).toBeDisabled();
  fireEvent.click(download); await screen.findByRole('link');
  fireEvent.click(screen.getByRole('button', { name: 'Show archived image preview' })); await screen.findByRole('img');
  view.rerender(<AttachmentManageControl {...p} version={6} />);
  expect(screen.queryByRole('link')).toBeNull(); expect(screen.queryByRole('img')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Check archived file download access' })).toBeNull();
  expect(vi.mocked(workRequest).mock.calls.every(([, init]) => !init?.method)).toBe(true);
});
it('ignores a late private admission when archive review is removed by a changed scope', async () => {
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(value => { resolve = value; }));
  const view = render(<FileAttachmentDownloadControl {...props} />);
  fireEvent.click(screen.getByRole('button', { name: 'Check archived file download access' }));
  await waitFor(() => expect(workRequest).toHaveBeenCalledOnce());
  view.rerender(<FileAttachmentDownloadControl {...props} cardId={id(9)} />);
  await act(async () => resolve(profile)); expect(screen.queryByRole('link')).toBeNull(); expect(workRequest).toHaveBeenCalledOnce();
});
