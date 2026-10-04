import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FileAttachmentCreateControl } from './FileAttachmentCreateControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
import { attachmentFileDigest } from './attachmentFileDigest';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
vi.mock('./attachmentFileDigest', () => ({ attachmentFileDigest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) }; const now = '2026-10-03T08:00:00.123456Z';
const options = { ...scope, cardVersion: 4, maximumBytes: 20971520, allowedMimeTypes: ['application/pdf'] };
const props = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false, onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const ack = () => ({ ...scope, cardVersion: 5, attachment: { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, uploaderId: profile.id,
  kind: 0, displayName: 'Résumé.png', url: null, mimeType: 'application/pdf', sizeBytes: 9, scanStatus: 1, scannedAt: null,
  createdAt: now, updatedAt: now, version: 1, deletedAt: null, lifecycleState: 0, archivedAt: null, deletedBy: null } });
const selected = () => new File(['%PDF-1.7\n'], 'Résumé.png', { type: 'image/png' });
const writes = () => vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/attachments'));
function respond(post: () => Promise<unknown> = async () => ack()) {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : path.endsWith('/attachment-upload-options') ? options : post());
}
beforeEach(() => { vi.mocked(workRequest).mockReset(); vi.mocked(attachmentFileDigest).mockReset().mockResolvedValue('a'.repeat(64)); });
afterEach(() => vi.useRealTimers());
async function review(file = selected()) {
  fireEvent.click(screen.getByRole('button', { name: 'Add file attachment' }));
  const input = await screen.findByLabelText('File to attach'); await waitFor(() => expect(input).toBeEnabled());
  fireEvent.change(input, { target: { files: [file] } }); return file;
}
it('reviews current options and actor, manages keyboard focus and sends actual File with original claims before admitting Pending receipt', async () => {
  respond(); const p = props(); render(<FileAttachmentCreateControl {...p} />);
  expect(workRequest).not.toHaveBeenCalled(); const file = await review();
  expect(writes()).toHaveLength(0); expect(screen.getByText(/Supported files: PDF/)).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  expect(await screen.findByText('File attached. Safety scan pending.')).toBeVisible();
  expect(writes()).toHaveLength(1); expect(writes()[0][1]!.body).toBe(file);
  const headers = writes()[0][1]!.headers as Record<string, string>;
  expect(headers['Content-Type']).toBe('application/octet-stream'); expect(headers['X-Attachment-Size']).toBe('9');
  expect(headers['X-Attachment-SHA256']).toBe('a'.repeat(64)); expect(headers['X-Card-Version']).toBe('4');
  expect(new TextDecoder().decode(Uint8Array.from(atob(headers['X-Attachment-Name']), c => c.charCodeAt(0)))).toBe(file.name);
  expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/); expect(attachmentFileDigest).toHaveBeenCalledOnce();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Add file attachment' })).toHaveFocus());
  expect(p.onRefresh).toHaveBeenCalledOnce(); expect(p.onBusyChange).toHaveBeenLastCalledWith(false);
});
it('purges the retained file and original retry when the account changes after publication', async () => {
  let profiles = 0;
  vi.mocked(workRequest).mockImplementation(async path => path === '/me'
    ? (++profiles < 4 ? profile : { ...profile, id: id(99) }) : path.endsWith('/attachment-upload-options') ? options : ack());
  render(<FileAttachmentCreateControl {...props()} />); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  await screen.findByText('This file upload is unavailable. Load the current Card before reviewing another change.');
  expect(writes()).toHaveLength(1); expect(screen.queryByLabelText('File to attach')).not.toBeInTheDocument();
  expect(screen.queryByText(/Selected file:/)).not.toBeInTheDocument();
  expect(screen.queryByText('File attached. Safety scan pending.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original file upload' })).not.toBeInTheDocument();
});
it.each([new WorkRequestError(503, null), new WorkRequestError(409, null, 'attachment_upload_in_progress'), new WorkRequestError(429, null)])('preserves one original File/actor/hash/key/revision through unconfirmed response and a newer hidden snapshot (%j)', async failure => {
    let attempts = 0; respond(async () => { if (++attempts === 1) throw failure; return ack(); });
    const p = props(); const view = render(<FileAttachmentCreateControl {...p} />); const file = await review();
    fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' })); await screen.findByRole('button', { name: 'Retry original file upload' });
    await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true)); expect(screen.getByLabelText('File to attach')).toBeDisabled();
    view.rerender(<FileAttachmentCreateControl {...p} version={5} unavailable />); expect(screen.queryByText(/Selected file:/)).not.toBeInTheDocument();
    view.rerender(<FileAttachmentCreateControl {...p} version={5} />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original file upload' })).toHaveFocus());
    fireEvent.click(screen.getByRole('button', { name: 'Retry original file upload' })); await screen.findByText('File attached. Safety scan pending.');
    expect(writes()).toHaveLength(2); expect(writes()[0][1]!.body).toBe(file); expect(writes()[1][1]!.body).toBe(file);
    expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers); expect(attachmentFileDigest).toHaveBeenCalledOnce();
    await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
  });
it.each([{ uploaderId: id(90) }, { sizeBytes: 10 }, { displayName: 'Changed' }, { storageKey: 'private/key' }, { scanStatus: 2, scannedAt: now, version: 2 }])('retains the original request when acknowledgment has different claims or private/terminal fields (%j)', async patch => {
    respond(async () => ({ ...ack(), attachment: { ...ack().attachment, ...patch } })); render(<FileAttachmentCreateControl {...props()} />);
    await review(); fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
    expect(await screen.findByRole('button', { name: 'Retry original file upload' })).toBeEnabled(); expect(writes()).toHaveLength(1);
    expect(screen.queryByText('File attached. Safety scan pending.')).not.toBeInTheDocument();
  });
it.each([2, 3])('refuses a different actor on profile read %s before emitting a file mutation', async changedRead => {
  let reads = 0; vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? ++reads === changedRead ? { ...profile, id: id(99) } : profile : options);
  const p = props(); render(<FileAttachmentCreateControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  await screen.findByText(/This file upload is unavailable/); expect(writes()).toHaveLength(0); expect(screen.queryByLabelText('File to attach')).not.toBeInTheDocument();
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true));
  expect(screen.queryByRole('button', { name: 'Retry original file upload' })).not.toBeInTheDocument();
});
it('refuses unscoped/private upload options without showing file selection or sending bytes', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : { ...options, storageKey: 'private/key' });
  render(<FileAttachmentCreateControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Add file attachment' }));
  await screen.findByText('File upload is unavailable. Refresh the Card and try again.'); expect(screen.queryByLabelText('File to attach')).not.toBeInTheDocument(); expect(writes()).toHaveLength(0);
});
it('blocks empty selection or changed original Card revision before hashing/transmitting and preserves the selected file until discard', async () => {
  respond(); const p = props(); const view = render(<FileAttachmentCreateControl {...p} />); await review(new File([], 'empty.pdf'));
  fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' })); await screen.findByText(/Choose a nonempty file/);
  expect(attachmentFileDigest).not.toHaveBeenCalled(); expect(writes()).toHaveLength(0);
  fireEvent.change(screen.getByLabelText('File to attach'), { target: { files: [selected()] } });
  view.rerender(<FileAttachmentCreateControl {...p} version={5} />); expect(screen.getByRole('button', { name: 'Upload selected file' })).toBeDisabled();
  expect(screen.getByText(/Selected file: Résumé.png/)).toBeVisible(); expect(writes()).toHaveLength(0);
});
it('cancels a stalled digest promptly, keeps the selection and ignores a late digest without sending bytes', async () => {
  respond(); let resolve: (digest: string) => void = () => {};
  vi.mocked(attachmentFileDigest).mockImplementation(() => new Promise<string>(yes => { resolve = yes; }));
  render(<FileAttachmentCreateControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  await screen.findByText('Preparing selected file…'); fireEvent.click(screen.getByRole('button', { name: 'Stop file upload' }));
  await screen.findByText('File preparation stopped. Your selected file is preserved.');
  await act(async () => resolve('a'.repeat(64))); expect(writes()).toHaveLength(0); expect(screen.getByText(/Selected file:/)).toBeVisible();
});
it('retains original bytes/key after stopping a stalled POST and ignores its late response', async () => {
  let resolve: (acknowledgment: unknown) => void = () => {}; let count = 0;
  respond(() => ++count === 1 ? new Promise<unknown>(yes => { resolve = yes; }) : Promise.resolve(ack()));
  const p = props(); render(<FileAttachmentCreateControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  await screen.findByText('Uploading file for a safety scan…'); fireEvent.click(screen.getByRole('button', { name: 'Stop file upload' }));
  await screen.findByText(/The file upload is unconfirmed/); expect((writes()[0][1]!.signal as AbortSignal).aborted).toBe(true);
  await act(async () => resolve(ack())); expect(screen.queryByText('File attached. Safety scan pending.')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry original file upload' })); await screen.findByText('File attached. Safety scan pending.');
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
});
it('bounds a stalled POST at five minutes without treating timeout as absence or generating a replacement intent', async () => {
  respond(() => new Promise<unknown>(() => {})); const p = props(); render(<FileAttachmentCreateControl {...p} />); await review();
  vi.useFakeTimers(); fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(0); }); expect(writes()).toHaveLength(1);
  await act(async () => { await vi.advanceTimersByTimeAsync(300000); });
  expect(screen.getByRole('button', { name: 'Retry original file upload' })).toBeEnabled();
  expect((writes()[0][1]!.signal as AbortSignal).aborted).toBe(true); expect(p.onBusyChange).toHaveBeenLastCalledWith(false);
  expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true); expect(attachmentFileDigest).toHaveBeenCalledOnce();
});
it('aborts on scope change and does not disclose late protected content in the new Card', async () => {
  respond(); let resolve: (digest: string) => void = () => {};
  vi.mocked(attachmentFileDigest).mockImplementation(() => new Promise<string>(yes => { resolve = yes; }));
  const p = props(); const view = render(<FileAttachmentCreateControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Upload selected file' }));
  await screen.findByText('Preparing selected file…'); view.rerender(<FileAttachmentCreateControl {...p} cardId={id(99)} />);
  await act(async () => resolve('a'.repeat(64))); expect(writes()).toHaveLength(0); expect(screen.queryByText(/Selected file:/)).not.toBeInTheDocument();
  expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
