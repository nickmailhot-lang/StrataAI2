import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { UrlAttachmentCreateControl } from './UrlAttachmentCreateControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) }; const now = '2026-10-03T08:00:00.123456Z';
const props = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false, onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const ack = () => ({ ...scope, cardVersion: 5, attachment: { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, uploaderId: profile.id,
  kind: 1, displayName: 'Reference', url: 'https://example.test/reference', mimeType: null, sizeBytes: null, scanStatus: 0, scannedAt: null,
  createdAt: now, updatedAt: now, version: 1, deletedAt: null } });
beforeEach(() => vi.mocked(workRequest).mockReset());
async function review() {
  fireEvent.click(screen.getByRole('button', { name: 'Add link attachment' }));
  fireEvent.change(await screen.findByLabelText(/New link attachment title/), { target: { value: ' Reference ' } });
  fireEvent.change(screen.getByLabelText(/Attachment URL/), { target: { value: ' https://example.test/reference ' } });
}
const writes = () => vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/attachments/url'));
it('creates a link only after review and validates actor/body/version acknowledgment before showing success', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : ack()); const p = props(); render(<UrlAttachmentCreateControl {...p} />);
  await review(); expect(writes()).toHaveLength(0); fireEvent.click(screen.getByRole('button', { name: 'Create link attachment' }));
  expect(await screen.findByText('Link attachment created.')).toBeVisible(); expect(writes()).toHaveLength(1);
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ title: 'Reference', url: 'https://example.test/reference', cardVersion: 4 });
  expect((writes()[0][1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Add link attachment' })).toHaveFocus()); expect(p.onRefresh).toHaveBeenCalledOnce();
});
it('retries only the same original actor/title/URL/revision/key across a newer snapshot and hidden re-admission', async () => {
  let count = 0; vi.mocked(workRequest).mockImplementation(async path => { if (path === '/me') return profile; if (++count === 1) throw new WorkRequestError(503, null); return ack(); });
  const p = props(); const view = render(<UrlAttachmentCreateControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Create link attachment' }));
  await screen.findByRole('button', { name: 'Retry link attachment creation' }); await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true));
  expect(screen.getByLabelText(/Attachment URL/)).toBeDisabled(); view.rerender(<UrlAttachmentCreateControl {...p} version={5} unavailable />);
  expect(screen.queryByLabelText(/Attachment URL/)).not.toBeInTheDocument(); expect(writes()).toHaveLength(1);
  view.rerender(<UrlAttachmentCreateControl {...p} version={5} />); await waitFor(() => expect(screen.getByRole('button', { name: 'Retry link attachment creation' })).toHaveFocus());
  expect(writes()).toHaveLength(1); fireEvent.click(screen.getByRole('button', { name: 'Retry link attachment creation' }));
  await screen.findByText('Link attachment created.'); expect(writes()).toHaveLength(2); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body); expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
});
it.each([{ uploaderId: id(90) }, { displayName: 'Changed' }, { url: 'https://example.test/changed' }, { storageKey: 'private/key' }])('keeps malformed or different-intent acknowledgment unresolved (%j)', async patch => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : { ...ack(), attachment: { ...ack().attachment, ...patch } });
  render(<UrlAttachmentCreateControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Create link attachment' }));
  expect(await screen.findByRole('button', { name: 'Retry link attachment creation' })).toBeEnabled(); expect(screen.queryByText('Link attachment created.')).not.toBeInTheDocument(); expect(writes()).toHaveLength(1);
});
it('refuses unsafe URLs locally and preserves both fields when the Card changes until explicit discard', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile); const p = props(); const view = render(<UrlAttachmentCreateControl {...p} />); await review();
  fireEvent.change(screen.getByLabelText(/Attachment URL/), { target: { value: 'javascript:alert(1)' } }); fireEvent.click(screen.getByRole('button', { name: 'Create link attachment' }));
  expect(await screen.findByText('Enter an HTTP(S) link without embedded credentials.')).toBeVisible(); expect(writes()).toHaveLength(0);
  fireEvent.change(screen.getByLabelText(/Attachment URL/), { target: { value: 'https://example.test/reference' } }); view.rerender(<UrlAttachmentCreateControl {...p} version={5} />);
  expect(screen.getByRole('button', { name: 'Create link attachment' })).toBeDisabled(); expect(screen.getByLabelText(/Attachment URL/)).toHaveValue('https://example.test/reference');
  fireEvent.click(screen.getByRole('button', { name: 'Discard title and URL and load latest' })); expect(screen.queryByLabelText(/Attachment URL/)).not.toBeInTheDocument(); expect(writes()).toHaveLength(0);
});
it('refuses a different signed-in actor before emitting a command and keeps reviewed fields blocked until explicit discard', async () => {
  let reads = 0; vi.mocked(workRequest).mockImplementation(async () => ++reads === 1 ? profile : { ...profile, id: id(99) });
  const p = props(); render(<UrlAttachmentCreateControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Create link attachment' }));
  await screen.findByText(/This link attachment change is unavailable/); expect(writes()).toHaveLength(0); expect(screen.getByLabelText(/Attachment URL/)).toBeDisabled();
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true)); expect(screen.queryByRole('button', { name: 'Retry link attachment creation' })).not.toBeInTheDocument();
});
