import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ChecklistCreateControl } from './ChecklistCreateControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const now = '2026-10-03T01:00:00.123456Z';
const props = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false,
  onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
const ack = () => ({ ...scope, changed: true, cardVersion: 5, checklist: { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId,
  title: 'Preparations', rank: '5'.padEnd(30, '0'), createdAt: now, updatedAt: now, version: 1, deletedAt: null } });
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
async function review() {
  fireEvent.click(screen.getByRole('button', { name: 'Add checklist' }));
  fireEvent.change(await screen.findByLabelText(/New checklist title/), { target: { value: ' Preparations ' } });
}
it('creates a scoped Checklist from a reviewed Card revision and returns focus', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : ack());
  const p = props(); render(<ChecklistCreateControl {...p} />); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Create checklist' }));
  expect(await screen.findByText('Checklist created.')).toBeVisible();
  const calls = vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/checklists'));
  expect(calls).toHaveLength(1); expect(JSON.parse(calls[0][1]!.body as string)).toEqual({ title: 'Preparations', cardVersion: 4 });
  expect((calls[0][1]!.headers as Record<string, string>)['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Add checklist' })).toHaveFocus());
  expect(p.onRefresh).toHaveBeenCalledOnce(); expect(p.onBusyChange).toHaveBeenLastCalledWith(false);
});
it('retries exactly the original body and key despite a newer snapshot and temporary re-admission', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async path => {
    if (path === '/me') return profile;
    if (++writes === 1) throw new WorkRequestError(0, null);
    return ack();
  });
  const p = props(); const view = render(<ChecklistCreateControl {...p} />); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Create checklist' }));
  await screen.findByRole('button', { name: 'Retry checklist creation' });
  expect(screen.getByLabelText(/New checklist title/)).toBeDisabled(); expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true);
  view.rerender(<ChecklistCreateControl {...p} version={5} unavailable />);
  expect(screen.queryByLabelText(/New checklist title/)).not.toBeInTheDocument();
  view.rerender(<ChecklistCreateControl {...p} version={5} />);
  const retry = screen.getByRole('button', { name: 'Retry checklist creation' });
  await waitFor(() => expect(retry).toHaveFocus()); fireEvent.click(retry);
  await screen.findByText('Checklist created.');
  const calls = vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/checklists'));
  expect(calls).toHaveLength(2); expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
  expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it.each([{ boardId: id(90) }, { cardVersion: 6 }, { changed: false }, { checklist: { ...ack().checklist, title: 'Other title' } }])('keeps the request unresolved when acknowledgment is not canonical (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : { ...ack(), ...change });
  render(<ChecklistCreateControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Create checklist' }));
  expect(await screen.findByRole('button', { name: 'Retry checklist creation' })).toBeEnabled();
  expect(screen.queryByText('Checklist created.')).not.toBeInTheDocument();
});
it('preserves a dirty title on a newer Card and requires explicit discard before review', async () => {
  vi.mocked(workRequest).mockResolvedValue(profile);
  const p = props(); const view = render(<ChecklistCreateControl {...p} />); await review();
  view.rerender(<ChecklistCreateControl {...p} version={5} />);
  expect(screen.getByLabelText(/New checklist title/)).toHaveValue(' Preparations ');
  expect(screen.getByRole('button', { name: 'Create checklist' })).toBeDisabled();
  expect(screen.getByRole('alert')).toHaveTextContent('title is preserved');
  fireEvent.click(screen.getByRole('button', { name: 'Discard checklist title and load latest' }));
  expect(screen.queryByLabelText(/New checklist title/)).not.toBeInTheDocument();
  expect(vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/checklists'))).toHaveLength(0);
});
it('restores recovery focus after re-admission without overriding intentional focus elsewhere', async () => {
  vi.mocked(workRequest).mockImplementation(async path => { if (path === '/me') return profile; throw new WorkRequestError(0, null); });
  const p = props(); const view = render(<><ChecklistCreateControl {...p} /><button>Other control</button></>); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Create checklist' }));
  const retry = await screen.findByRole('button', { name: 'Retry checklist creation' }); await waitFor(() => expect(retry).toHaveFocus());
  const other = screen.getByRole('button', { name: 'Other control' }); act(() => other.focus());
  view.rerender(<><ChecklistCreateControl {...p} unavailable /><button>Other control</button></>);
  view.rerender(<><ChecklistCreateControl {...p} /><button>Other control</button></>);
  expect(other).toHaveFocus();
});
it('rejects a changed actor before writing and requires current review after a definite denial', async () => {
  let profiles = 0; vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? { ...profile, id: ++profiles === 1 ? profile.id : id(9) } : ack());
  const p = props(); render(<ChecklistCreateControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Create checklist' }));
  expect(await screen.findByText(/This checklist change is unavailable/)).toBeVisible();
  expect(screen.getByLabelText(/New checklist title/)).toHaveValue(' Preparations ');
  expect(screen.getByRole('button', { name: 'Create checklist' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Retry checklist creation' })).not.toBeInTheDocument();
  expect(vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/checklists'))).toHaveLength(0);
});
it('prevents double submission and cancels the owning request when the Card changes', async () => {
  let resolve: (value: unknown) => void = () => {}; let signal: AbortSignal | undefined;
  vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile;
    signal = options?.signal as AbortSignal; return new Promise(yes => { resolve = yes; });
  });
  const p = props(); const view = render(<ChecklistCreateControl {...p} />); await review();
  const save = screen.getByRole('button', { name: 'Create checklist' }); fireEvent.click(save); fireEvent.click(save);
  await waitFor(() => expect(vi.mocked(workRequest).mock.calls.filter(([path]) => path.endsWith('/checklists'))).toHaveLength(1));
  view.rerender(<ChecklistCreateControl {...p} cardId={id(90)} />); expect(signal?.aborted).toBe(true);
  await act(async () => resolve(ack())); expect(screen.queryByText('Checklist created.')).not.toBeInTheDocument();
});
