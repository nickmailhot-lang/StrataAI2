import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { workRequest } from '../../api/workManagement';
import { FileAttachmentPreviewControl } from './FileAttachmentPreviewControl';
import type { FileAttachment } from './attachments';

vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => '00000000-0000-4000-8000-' + String(n).padStart(12, '0');
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const now = '2026-10-03T08:00:00.123456Z';
const file: FileAttachment = { id: id(4), organizationId: id(1), cardId: id(3), uploaderId: profile.id, kind: 0, displayName: 'Photo.webp',
  mimeType: 'image/webp', sizeBytes: 100003, url: null, scanStatus: 2, scannedAt: now, createdAt: now, updatedAt: now, version: 3, deletedAt: null, lifecycleState: 0, archivedAt: null, deletedBy: null };
const options = { ...scope, cardVersion: 4, attachmentId: file.id, attachmentVersion: file.version, actorId: profile.id };
const props = { ...scope, version: 4, file, onRefresh: vi.fn() };
beforeEach(() => { vi.mocked(workRequest).mockReset(); props.onRefresh.mockReset(); });
afterEach(() => vi.useRealTimers());
function admit() { vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce(options).mockResolvedValueOnce(profile); }
function check() { fireEvent.click(screen.getByRole('button', { name: 'Show image preview' })); }

it('checks current actor and published scope before requesting a native sanitized image, then hides it with keyboard focus', async () => {
  admit(); render(<FileAttachmentPreviewControl {...props} />); expect(workRequest).not.toHaveBeenCalled(); expect(screen.queryByRole('img')).toBeNull();
  check(); const image = await screen.findByRole('img', { name: 'Sanitized preview of Photo.webp' });
  expect(image).toHaveAttribute('src', '/cards/' + scope.cardId + '/attachments/' + file.id + '/preview?actorId=' + profile.id + '&attachmentVersion=3');
  expect(image).toHaveAttribute('referrerpolicy', 'no-referrer');
  expect(vi.mocked(workRequest).mock.calls.map(call => call[0])).toEqual(['/me', '/cards/' + scope.cardId + '/attachments/' + file.id + '/preview-options', '/me']);
  fireEvent.load(image); expect(screen.getByRole('status')).toHaveTextContent('Image preview loaded.');
  fireEvent.click(screen.getByRole('button', { name: 'Hide image preview' }));
  expect(screen.queryByRole('img')).toBeNull(); expect(screen.getByRole('button', { name: 'Show image preview' })).toHaveFocus();
});

it.each(['organizationId', 'boardId', 'cardId', 'attachmentId', 'actorId', 'cardVersion', 'attachmentVersion', 'privateExtra'])('refuses changed or extra options field %s', async field => {
  vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce({ ...options, [field]: field.endsWith('Version') ? 99 : id(99) });
  render(<FileAttachmentPreviewControl {...props} />); check();
  expect(await screen.findByRole('alert')).toHaveTextContent('Image preview is unavailable.');
  expect(screen.queryByRole('img')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Refresh Card for image preview' })); expect(props.onRefresh).toHaveBeenCalledOnce();
});

it('refuses an actor change after publication options and handles a failed image without leaving the private request visible', async () => {
  vi.mocked(workRequest).mockResolvedValueOnce(profile).mockResolvedValueOnce(options).mockResolvedValueOnce({ ...profile, id: id(9) });
  render(<FileAttachmentPreviewControl {...props} />); check(); await screen.findByRole('alert'); expect(screen.queryByRole('img')).toBeNull();
  admit(); check(); const image = await screen.findByRole('img'); fireEvent.error(image);
  expect(screen.queryByRole('img')).toBeNull(); expect(screen.getByRole('alert')).toHaveTextContent('Image preview could not be loaded.');
});

it('retires the image on Card revisions and ignores an old scope result', async () => {
  admit(); const view = render(<FileAttachmentPreviewControl {...props} />); check(); await screen.findByRole('img');
  view.rerender(<FileAttachmentPreviewControl {...props} version={5} />); expect(screen.queryByRole('img')).toBeNull();
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(value => { resolve = value; })); check();
  await waitFor(() => expect(workRequest).toHaveBeenCalledTimes(4));
  view.rerender(<FileAttachmentPreviewControl {...props} cardId={id(9)} />);
  await act(async () => resolve(profile)); expect(screen.queryByRole('img')).toBeNull(); expect(workRequest).toHaveBeenCalledTimes(4);
});

it('stops a stalled review and leaves focus that moved to another action alone', async () => {
  let resolve!: (value: unknown) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(value => { resolve = value; }));
  render(<><FileAttachmentPreviewControl {...props} /><button>Other action</button></>); check();
  await waitFor(() => expect(workRequest).toHaveBeenCalledOnce());
  fireEvent.click(screen.getByRole('button', { name: 'Stop preview review' }));
  await act(async () => resolve(profile)); expect(screen.queryByRole('img')).toBeNull();
  vi.mocked(workRequest).mockResolvedValueOnce(profile).mockImplementationOnce(() => new Promise(value => { resolve = value; })).mockResolvedValueOnce(profile);
  check(); await act(async () => { await Promise.resolve(); });
  const other = screen.getByRole('button', { name: 'Other action' }); other.focus();
  await act(async () => resolve(options)); await screen.findByRole('img'); expect(other).toHaveFocus();
});

it('expires even a loaded preview and requires new admission', async () => {
  admit(); render(<FileAttachmentPreviewControl {...props} />); check(); await screen.findByRole('img');
  vi.useFakeTimers(); admit(); check();
  await act(async () => { await Promise.resolve(); await Promise.resolve(); });
  await act(async () => vi.advanceTimersByTime(60000));
  expect(screen.queryByRole('img')).toBeNull(); expect(screen.getByRole('status')).toHaveTextContent('Preview review expired.');
});

it.each([1, 3, 4])('does not offer previews for scan state %s', scanStatus => {
  const changed = { ...file, scanStatus, scannedAt: scanStatus === 1 ? null : now } as FileAttachment;
  render(<FileAttachmentPreviewControl {...props} file={changed} />); expect(screen.queryByRole('button')).toBeNull(); expect(workRequest).not.toHaveBeenCalled();
});
it('does not offer a PDF image preview', () => {
  render(<FileAttachmentPreviewControl {...props} file={{ ...file, mimeType: 'application/pdf' }} />);
  expect(screen.queryByRole('button')).toBeNull(); expect(workRequest).not.toHaveBeenCalled();
});
