import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Button, Dialog } from '@mui/material';
import { AttachmentManageControl } from './AttachmentManageControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const created = '2026-10-03T08:00:00.123456Z'; const at = '2026-10-03T08:01:00.123456Z'; const later = '2026-10-03T08:02:00.123456Z';
const file = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, uploaderId: profile.id,
  kind: 1, displayName: 'Reference', url: 'https://example.test/', mimeType: null, sizeBytes: null, scanStatus: 0, scannedAt: null,
  createdAt: created, updatedAt: created, version: 1, lifecycleState: 0, archivedAt: null, deletedAt: null, deletedBy: null };
const archivedFile = { ...file, lifecycleState: 1, version: 2, updatedAt: at, archivedAt: at };
const page = { ...scope, cardVersion: 4, items: [file], nextCursor: null, canEdit: true };
const archivePage = { ...scope, cardVersion: 4, items: [archivedFile], nextCursor: null, canRestore: true, canDelete: true };
const ack = { ...scope, cardVersion: 5, changed: true, attachment: archivedFile };
const props = () => ({ ...scope, version: 4, editable: true, canAdminister: true, disabled: false, unavailable: false,
  onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const writes = () => vi.mocked(workRequest).mock.calls.filter(([, init]) => !!init?.method);
function mock(write: (path: string) => unknown = () => ack) {
  vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? profile : init?.method ? write(path)
    : path.endsWith('/archive') ? archivePage : page);
}
async function select(action = 'Archive') {
  fireEvent.click(screen.getByRole('button', { name: 'Manage attachments' }));
  if (action !== 'Archive') fireEvent.click(await screen.findByRole('button', { name: 'Review attachment archive' }));
  fireEvent.click(await screen.findByRole('button', { name: `${action} attachment Reference` }));
}
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
it('withholds private attachment review when the account changes during the read', async () => {
  let profiles = 0;
  vi.mocked(workRequest).mockImplementation(async path => path === '/me'
    ? (++profiles === 1 ? profile : { ...profile, id: '99999999-9999-4999-8999-999999999999' }) : page);
  render(<AttachmentManageControl {...props()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Manage attachments' }));
  await screen.findByText('Unable to review current attachments. Refresh the Card and try again.');
  expect(screen.queryByText('Reference')).not.toBeInTheDocument(); expect(writes()).toHaveLength(0);
});
it('purges private review and original retry when the account changes after a committed response', async () => {
  let profiles = 0;
  vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me'
    ? (++profiles < 4 ? profile : { ...profile, id: '99999999-9999-4999-8999-999999999999' }) : init?.method ? ack : page);
  render(<AttachmentManageControl {...props()} />); await select();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm attachment archive' }));
  await screen.findByText('This attachment change is unavailable. Load the latest Card before reviewing another change.');
  expect(writes()).toHaveLength(1); expect(screen.queryByText('Reference')).not.toBeInTheDocument();
  expect(screen.queryByText('Attachment archived.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original attachment change' })).not.toBeInTheDocument();
});
it('reviews current metadata then sends the original archive revisions/key and admits the exact acknowledgment', async () => {
  mock(); const p = props(); render(<AttachmentManageControl {...p} />); expect(workRequest).not.toHaveBeenCalled();
  await select(); expect(writes()).toHaveLength(0); fireEvent.click(screen.getByRole('button', { name: 'Confirm attachment archive' }));
  await screen.findByText('Attachment archived.'); expect(writes()).toHaveLength(1);
  expect(writes()[0][0]).toBe(`/cards/${scope.cardId}/attachments/${file.id}/archive`);
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ cardVersion: 4, version: 1 });
  expect((writes()[0][1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  expect(p.onRefresh).toHaveBeenCalledOnce(); await waitFor(() => expect(p.onBusyChange).toHaveBeenLastCalledWith(false));
});
it('requires a fresh explicit irreversible-deletion checkbox and uses the scoped canonical DELETE', async () => {
  mock(() => ({ ...scope, cardVersion: 5, changed: true, attachment: { ...archivedFile, lifecycleState: 2, version: 3, updatedAt: later, deletedAt: later, deletedBy: profile.id } }));
  render(<AttachmentManageControl {...props()} />); await select('Delete');
  const save = screen.getByRole('button', { name: 'Permanently delete attachment' }); expect(save).toBeDisabled(); expect(writes()).toHaveLength(0);
  expect(screen.queryByText(/Board backgrounds using this image/)).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('checkbox', { name: /cannot be undone/ })); expect(save).toBeEnabled(); fireEvent.click(save);
  await screen.findByText('Attachment permanently deleted.');
  expect(writes()[0][0]).toBe(`/attachments/${file.id}?cardId=${scope.cardId}&cardVersion=4&version=2&confirmed=true`);
  expect(writes()[0][1]!.method).toBe('DELETE'); expect(writes()[0][1]!.body).toBeUndefined();
});
it.each(['image/png', 'image/jpeg', 'image/webp', 'application/pdf'])('explains retained Board image copies before deletion consent for %s', async mimeType => {
  const attachment = { ...archivedFile, kind: 0, mimeType, url: null, sizeBytes: 123, scanStatus: 2, scannedAt: created };
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile
    : path.endsWith('/archive') ? { ...archivePage, items: [attachment] } : page);
  render(<AttachmentManageControl {...props()} />); await select('Delete');
  expect(screen.getByRole('button', { name: 'Permanently delete attachment' })).toBeDisabled();
  expect(screen.getByRole('checkbox', { name: /cannot be undone/ })).not.toBeChecked();
  if (mimeType === 'application/pdf') expect(screen.queryByText(/Board backgrounds using this image/)).not.toBeInTheDocument();
  else expect(screen.getByText(/Board backgrounds using this image keep their copies/)).toBeVisible();
  expect(writes()).toHaveLength(0);
});
it('restores retained archive history without fabricating a new source or scan state', async () => {
  mock(() => ({ ...scope, cardVersion: 5, changed: true, attachment: { ...archivedFile, lifecycleState: 0, version: 3, updatedAt: later } }));
  render(<AttachmentManageControl {...props()} />); await select('Restore'); fireEvent.click(screen.getByRole('button', { name: 'Confirm attachment restore' }));
  await screen.findByText('Attachment restored.'); expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ cardVersion: 4, version: 2 });
});
it('keeps the exact request across unknown replies/newer snapshots and resolves without stealing another control focus', async () => {
  let count = 0; mock(() => { if (++count === 1) throw new WorkRequestError(503, null); return ack; });
  const p = props(); const view = render(<Dialog open><AttachmentManageControl {...p} /><Button>Another control</Button></Dialog>);
  await select(); const save = screen.getByRole('button', { name: 'Confirm attachment archive' }); save.focus(); fireEvent.click(save);
  const retry = await screen.findByRole('button', { name: 'Retry original attachment change' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.queryByRole('button', { name: 'Discard attachment review and load latest' })).not.toBeInTheDocument();
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true));
  view.rerender(<Dialog open><AttachmentManageControl {...p} version={9} unavailable /><Button>Another control</Button></Dialog>);
  expect(screen.queryByText('Reference')).not.toBeInTheDocument();
  screen.getByRole('button', { name: 'Another control' }).focus();
  view.rerender(<Dialog open><AttachmentManageControl {...p} version={9} /><Button>Another control</Button></Dialog>);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original attachment change' })).toBeEnabled());
  expect(screen.getByRole('button', { name: 'Another control' })).toHaveFocus();
  fireEvent.click(screen.getByRole('button', { name: 'Retry original attachment change' })); await screen.findByText('Attachment archived.');
  expect(writes()).toHaveLength(2); expect(writes()[1][0]).toBe(writes()[0][0]); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers); await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
});
it.each([{ version: 3 }, { storageKey: 'private/key' }, { archivedAt: null }, { uploaderId: id(99) }])('retains the original intent for malformed acknowledgment %j', async patch => {
  mock(() => ({ ...ack, attachment: { ...ack.attachment, ...patch } })); render(<AttachmentManageControl {...props()} />); await select();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm attachment archive' }));
  await screen.findByRole('button', { name: 'Retry original attachment change' }); expect(screen.queryByText('Attachment archived.')).not.toBeInTheDocument();
});
it('blocks a stale reviewed draft and permits explicit discard after a conclusive conflict', async () => {
  mock(() => { throw new WorkRequestError(409, null); }); const p = props(); const view = render(<AttachmentManageControl {...p} />); await select();
  view.rerender(<AttachmentManageControl {...p} version={5} />); expect(screen.getByRole('button', { name: 'Confirm attachment archive' })).toBeDisabled(); expect(writes()).toHaveLength(0);
  view.rerender(<AttachmentManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Confirm attachment archive' }));
  await screen.findByText(/This attachment change is unavailable/); expect(screen.queryByRole('button', { name: 'Retry original attachment change' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Discard attachment review and load latest' }));
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false)); expect(writes()).toHaveLength(1);
});
it('permits read-only archive review but refuses mutation capabilities, and never sends after an actor switch', async () => {
  mock(); const p = props(); const view = render(<AttachmentManageControl {...p} editable={false} canAdminister={false} />);
  fireEvent.click(screen.getByRole('button', { name: 'Manage attachments' })); fireEvent.click(await screen.findByRole('button', { name: 'Review attachment archive' }));
  expect(await screen.findByRole('button', { name: 'Restore attachment Reference' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Delete attachment Reference' })).toBeDisabled();
  view.unmount(); vi.mocked(workRequest).mockReset(); let reads = 0;
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? ++reads <= 2 ? profile : { ...profile, id: id(99) } : page);
  render(<AttachmentManageControl {...props()} />); await select(); fireEvent.click(screen.getByRole('button', { name: 'Confirm attachment archive' }));
  await screen.findByText(/This attachment change is unavailable/); expect(writes()).toHaveLength(0);
});
