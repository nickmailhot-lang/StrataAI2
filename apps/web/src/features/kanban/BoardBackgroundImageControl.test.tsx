import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Dialog } from '@mui/material';
import { BoardBackgroundImageControl } from './BoardBackgroundImageControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const candidate = { attachmentId: id(4), attachmentVersion: 3, displayName: 'Checked image.png', createdAt: '2026-10-03T08:00:00.123456Z' };
const board = { id: scope.boardId, organizationId: scope.organizationId, version: 7, name: 'Board', description: null,
  lifecycleState: 'active', visibility: 'PRIVATE', backgroundType: 'COLOR', backgroundValue: 'blue' };
const source = { board, access: { canView: true, canEdit: true }, lists: [] };
const page = { ...scope, cardVersion: 4, items: [candidate], nextCursor: null, canEdit: true, isPublic: false };
const ack = { ...board, version: 8, backgroundType: 'IMAGE', backgroundValue: id(9) };
const props = () => ({ ...scope, version: 4, boardVersion: 7, editable: true, disabled: false, unavailable: false,
  onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() });
const writes = () => vi.mocked(workRequest).mock.calls.filter(([, init]) => !!init?.method);
function mock(write: () => unknown = () => ack, visibility = 'PRIVATE') {
  vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? profile : init?.method ? write()
    : path.includes('/candidates') ? { ...page, isPublic: visibility === 'PUBLIC' } : { ...source, board: { ...board, visibility } });
}
async function choose() {
  fireEvent.click(screen.getByRole('button', { name: 'Review Board background images' }));
  const choice = await screen.findByRole('button', { name: 'Use Checked image.png as Board background' });
  choice.focus(); fireEvent.click(choice);
}
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
it('reviews current Board and checked source before sending a scoped atomic image intent', async () => {
  mock(); const p = props(); render(<BoardBackgroundImageControl {...p} />); expect(workRequest).not.toHaveBeenCalled();
  await choose(); expect(writes()).toHaveLength(0); fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  await screen.findByText('Board background updated.'); expect(writes()).toHaveLength(1);
  expect(writes()[0][0]).toBe(`/boards/${scope.boardId}/background/image`);
  expect(new Headers(writes()[0][1]!.headers).get('Content-Type')).toBe('application/json');
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ cardId: scope.cardId, attachmentId: candidate.attachmentId,
    attachmentVersion: 3, boardVersion: 7, publicVisibilityConfirmed: false });
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
});
it('requires explicit PUBLIC consent and records it in the original intent', async () => {
  mock(() => ({ ...ack, visibility: 'PUBLIC' }), 'PUBLIC'); render(<BoardBackgroundImageControl {...props()} />); await choose();
  const save = screen.getByRole('button', { name: 'Confirm Board background image' }); expect(save).toBeDisabled();
  fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this Board background image will be publicly visible' }));
  fireEvent.click(save); await screen.findByText('Board background updated.');
  expect(JSON.parse(writes()[0][1]!.body as string).publicVisibilityConfirmed).toBe(true);
});
it('preserves the original request through an uncertain reply and newer Board/Card versions', async () => {
  let attempts = 0; mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; });
  const p = props(); const view = render(<BoardBackgroundImageControl {...p} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  const retry = await screen.findByRole('button', { name: 'Retry original Board background change' });
  await waitFor(() => expect(retry).toBeEnabled());
  view.rerender(<BoardBackgroundImageControl {...p} version={10} boardVersion={12} />);
  fireEvent.click(retry); await screen.findByText('Board background updated.');
  expect(writes()).toHaveLength(2); expect(writes()[0][1]!.body).toBe(writes()[1][1]!.body);
  expect(writes()[0][1]!.headers).toEqual(writes()[1][1]!.headers);
});
it.each([{ ...ack, id: id(10) }, { ...ack, backgroundValue: 'https://private.example/image' }, { ...ack, version: 9 }])('does not accept a mismatched image acknowledgment: %j', async value => {
  mock(() => value);
  render(<BoardBackgroundImageControl {...props()} />); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  await screen.findByText('The background change is unconfirmed. Retry the original change to recover its acknowledgment.');
  expect(screen.queryByText('Board background updated.')).not.toBeInTheDocument();
});
it('retires an uncertain intent before a changed account can resubmit it', async () => {
  mock(() => { throw new Error('Private diagnostics'); }); const p = props(); render(<BoardBackgroundImageControl {...p} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  const retry = await screen.findByRole('button', { name: 'Retry original Board background change' }); await waitFor(() => expect(retry).toBeEnabled());
  vi.mocked(workRequest).mockImplementation(async () => ({ ...profile, id: id(10) })); fireEvent.click(retry);
  await screen.findByText('This background change is unavailable. Review the current Board and images before another change.');
  expect(writes()).toHaveLength(1); expect(screen.queryByText('Private diagnostics')).not.toBeInTheDocument();
});
it('recovers the original intent through a background refresh using fresh Board admission', async () => {
  let attempts = 0; mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; });
  const p = props(); const view = render(<BoardBackgroundImageControl {...p} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  const retry = await screen.findByRole('button', { name: 'Retry original Board background change' });
  await waitFor(() => expect(retry).toBeEnabled());
  let release!: (value: typeof profile) => void;
  vi.mocked(workRequest).mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  fireEvent.click(retry);
  await waitFor(() => expect(release).toBeTypeOf('function'));
  view.rerender(<BoardBackgroundImageControl {...p} unavailable version={10} boardVersion={12} />);
  release(profile); await screen.findByText('Board background updated.');
  expect(writes()).toHaveLength(2); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers);
});
it('refuses a retry when fresh Board admission is withdrawn during its account proof', async () => {
  mock(() => { throw new WorkRequestError(503, null); }); render(<BoardBackgroundImageControl {...props()} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  const retry = await screen.findByRole('button', { name: 'Retry original Board background change' });
  await waitFor(() => expect(retry).toBeEnabled());
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : { ...source, access: { canView: true, canEdit: false } });
  fireEvent.click(retry);
  await screen.findByText('This background change is unavailable. Review the current Board and images before another change.');
  expect(writes()).toHaveLength(1);
});
it('requires fresh review after a known stale Board conflict', async () => {
  mock(() => { throw new WorkRequestError(409, null); }); render(<BoardBackgroundImageControl {...props()} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Board background image' }));
  await screen.findByText('This background change is unavailable. Review the current Board and images before another change.');
  expect(screen.queryByRole('button', { name: 'Retry original Board background change' })).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review Board background images' })).toBeEnabled());
});
it('withdraws the selected source when edit admission is lost', async () => {
  mock(); const p = props(); const view = render(<BoardBackgroundImageControl {...p} />); await choose();
  view.rerender(<BoardBackgroundImageControl {...p} editable={false} />);
  await screen.findByText('Board background changes are unavailable.'); expect(screen.queryByText('Checked image.png')).not.toBeInTheDocument();
  expect(writes()).toHaveLength(0);
});
it('moves keyboard focus to explicit PUBLIC consent inside Card details', async () => {
  mock(() => ({ ...ack, visibility: 'PUBLIC' }), 'PUBLIC');
  render(<Dialog open><BoardBackgroundImageControl {...props()} /></Dialog>); await choose();
  await waitFor(() => expect(screen.getByRole('checkbox', { name: 'I understand this Board background image will be publicly visible' })).toHaveFocus());
});

it('retains return-focus ownership through a post-acknowledgment access refresh without stealing another control', async () => {
  let attempts = 0; mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; });
  const p = props();
  const content = (unavailable: boolean) => <Dialog open><BoardBackgroundImageControl {...p} unavailable={unavailable} /><button>Other Card action</button></Dialog>;
  const view = render(content(false)); await choose();
  const confirm = screen.getByRole('button', { name: 'Confirm Board background image' }); confirm.focus(); fireEvent.click(confirm);
  const retry = await screen.findByRole('button', { name: 'Retry original Board background change' });
  await waitFor(() => expect(retry).toHaveFocus()); fireEvent.click(retry);
  await screen.findByText('Board background updated.');
  const primary = screen.getByRole('button', { name: 'Review Board background images' });
  await waitFor(() => expect(primary).toHaveFocus());
  view.rerender(content(true)); act(() => screen.getByRole('dialog').focus());
  view.rerender(content(false)); await waitFor(() => expect(primary).toHaveFocus());
  const other = screen.getByRole('button', { name: 'Other Card action' }); act(() => other.focus());
  view.rerender(content(true)); view.rerender(content(false)); expect(other).toHaveFocus();
  expect(writes()).toHaveLength(2); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
});
