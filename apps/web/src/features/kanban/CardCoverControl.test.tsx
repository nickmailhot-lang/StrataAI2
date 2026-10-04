import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Button, Dialog } from '@mui/material';
import { CardCoverControl } from './CardCoverControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const candidate = { attachmentId: id(4), attachmentVersion: 3, displayName: 'Site image.png', createdAt: '2026-10-03T08:00:00.123456Z' };
const base = { ...scope, cardVersion: 4, attachmentId: null as string | null, attachmentVersion: null as number | null, canEdit: true, isPublic: false };
const ack = { ...scope, cardVersion: 5, attachmentId: candidate.attachmentId, attachmentVersion: 3, changed: true };
const props = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false, onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const writes = () => vi.mocked(workRequest).mock.calls.filter(([, init]) => !!init?.method);
function mock(write: () => unknown = () => ack, view = base) {
  vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? profile : init?.method ? write()
    : path.includes('/candidates') ? { ...scope, cardVersion: view.cardVersion, items: [candidate], nextCursor: null, canEdit: view.canEdit, isPublic: view.isPublic } : view);
}
async function choose() {
  fireEvent.click(screen.getByRole('button', { name: 'Review Card cover' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Use Site image.png as cover' }));
}
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
it('reviews current authority/versions then acknowledges the exact chosen cover without unsolicited requests', async () => {
  mock(); const p = props(); render(<CardCoverControl {...p} />); expect(workRequest).not.toHaveBeenCalled();
  await choose(); expect(writes()).toHaveLength(0); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card cover' }));
  await screen.findByText('Card cover updated.'); expect(writes()).toHaveLength(1);
  expect(writes()[0][0]).toBe(`/cards/${scope.cardId}/cover`); expect(writes()[0][1]!.method).toBe('PUT');
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ attachmentId: candidate.attachmentId, attachmentVersion: 3, cardVersion: 4, publicVisibilityConfirmed: false });
  expect((writes()[0][1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false)); expect(p.onRefresh).toHaveBeenCalledOnce();
});
it('requires explicit fresh PUBLIC consent and captures it into the immutable command', async () => {
  mock(() => ack, { ...base, isPublic: true }); render(<CardCoverControl {...props()} />); await choose();
  const save = screen.getByRole('button', { name: 'Confirm Card cover' }); expect(save).toBeDisabled(); expect(writes()).toHaveLength(0);
  expect(screen.getByText(/including visitors who are not signed in/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole('checkbox', { name: 'I understand this cover image will be publicly visible' }));
  fireEvent.click(save); await screen.findByText('Card cover updated.'); expect(JSON.parse(writes()[0][1]!.body as string).publicVisibilityConfirmed).toBe(true);
});
it('removes the selected cover without a source revision or PUBLIC selection consent', async () => {
  mock(() => ({ ...ack, attachmentId: null, attachmentVersion: null }), { ...base, attachmentId: id(4), attachmentVersion: 3, isPublic: true });
  render(<CardCoverControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Review Card cover' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Remove Card cover' })); expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm cover removal' })); await screen.findByText('Card cover removed.');
  expect(JSON.parse(writes()[0][1]!.body as string)).toEqual({ attachmentId: null, attachmentVersion: null, cardVersion: 4, publicVisibilityConfirmed: false });
});
it('retains original intent/key/consent across lost replies and newer snapshots and respects another focus owner', async () => {
  let attempts = 0; mock(() => { if (++attempts === 1) throw new WorkRequestError(503, null); return ack; }, { ...base, isPublic: true });
  const p = props(); const view = render(<Dialog open><CardCoverControl {...p} /><Button>Another control</Button></Dialog>);
  await choose(); fireEvent.click(screen.getByRole('checkbox')); const save = screen.getByRole('button', { name: 'Confirm Card cover' }); save.focus(); fireEvent.click(save);
  const retry = await screen.findByRole('button', { name: 'Retry original cover change' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.queryByRole('button', { name: 'Discard cover review and load latest' })).not.toBeInTheDocument();
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true));
  view.rerender(<Dialog open><CardCoverControl {...p} version={9} unavailable /><Button>Another control</Button></Dialog>);
  expect(screen.queryByText('Site image.png')).not.toBeInTheDocument(); screen.getByRole('button', { name: 'Another control' }).focus();
  view.rerender(<Dialog open><CardCoverControl {...p} version={9} /><Button>Another control</Button></Dialog>);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original cover change' })).toBeEnabled());
  expect(screen.getByRole('button', { name: 'Another control' })).toHaveFocus(); fireEvent.click(screen.getByRole('button', { name: 'Retry original cover change' }));
  await screen.findByText('Card cover updated.'); expect(writes()).toHaveLength(2); expect(writes()[1][1]!.body).toBe(writes()[0][1]!.body);
  expect(writes()[1][1]!.headers).toEqual(writes()[0][1]!.headers); await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
});
it.each([{ cardVersion: 6 }, { attachmentVersion: 4 }, { storageKey: 'private/key' }, { changed: false }])('retains original intent for malformed receipt %j', async patch => {
  mock(() => ({ ...ack, ...patch })); render(<CardCoverControl {...props()} />); await choose();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm Card cover' })); await screen.findByRole('button', { name: 'Retry original cover change' });
  expect(screen.queryByText('Card cover updated.')).not.toBeInTheDocument(); expect(writes()).toHaveLength(1);
});
it('blocks a stale draft and supports explicit fresh review after a conclusive refusal', async () => {
  mock(() => { throw new WorkRequestError(409, null); }); const p = props(); const view = render(<CardCoverControl {...p} />); await choose();
  view.rerender(<CardCoverControl {...p} version={5} />); expect(screen.getByRole('button', { name: 'Confirm Card cover' })).toBeDisabled();
  view.rerender(<CardCoverControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card cover' })); await screen.findByText(/This cover change is unavailable/);
  expect(screen.queryByRole('button', { name: 'Retry original cover change' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Discard cover review and load latest' })); await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
});
it('permits readonly cover review and never sends a captured change after the actor switches', async () => {
  mock(); const view = render(<CardCoverControl {...props()} editable={false} />); fireEvent.click(screen.getByRole('button', { name: 'Review Card cover' }));
  expect(await screen.findByRole('button', { name: 'Use Site image.png as cover' })).toBeDisabled(); view.unmount();
  let profiles = 0; vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? ++profiles <= 2 ? profile : { ...profile, id: id(99) }
    : init?.method ? ack : path.includes('/candidates') ? { ...scope, cardVersion: 4, items: [candidate], nextCursor: null, canEdit: true, isPublic: false } : base);
  render(<CardCoverControl {...props()} />); await choose(); fireEvent.click(screen.getByRole('button', { name: 'Confirm Card cover' }));
  await screen.findByText(/This cover change is unavailable/); expect(writes()).toHaveLength(0);
  expect(screen.queryByText(candidate.displayName)).not.toBeInTheDocument();
});

it.each(['changed account', 'unavailable account', 'access refusal'])('purges private cover review after %s and supports fresh admission', async failure => {
  let profiles = 0;
  vi.mocked(workRequest).mockImplementation(async (path, init) => {
    if (path === '/me') {
      if (++profiles === 4) {
        if (failure === 'unavailable account') throw new WorkRequestError(503, null);
        return { ...profile, id: id(99) };
      }
      return profile;
    }
    if (init?.method) {
      if (failure === 'access refusal') throw new WorkRequestError(403, null);
      return ack;
    }
    return path.includes('/candidates') ? { ...scope, cardVersion: 4, items: [candidate], nextCursor: null, canEdit: true, isPublic: false } : base;
  });
  const p = props(); render(<CardCoverControl {...p} />); await choose();
  const save = screen.getByRole('button', { name: 'Confirm Card cover' }); save.focus(); fireEvent.click(save);
  await screen.findByText(/This cover change is unavailable/);
  expect(screen.queryByText(candidate.displayName)).not.toBeInTheDocument();
  expect(screen.queryByText('Card cover updated.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original cover change' })).not.toBeInTheDocument();
  const latest = screen.getByRole('button', { name: 'Load latest Card before reviewing a cover' });
  await waitFor(() => expect(latest).toHaveFocus());
  expect(writes()).toHaveLength(1); await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true));
  fireEvent.click(latest); await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
  expect(screen.getByRole('button', { name: 'Review Card cover' })).toBeEnabled();
});
