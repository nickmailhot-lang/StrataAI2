import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ChecklistManageControl } from './ChecklistManageControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const now = '2026-10-03T01:00:00.123456Z'; const rank = (n: number) => String(n).padStart(30, '0');
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const checklist = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, title: 'Preparations', rank: rank(1),
  createdAt: now, updatedAt: now, version: 2, deletedAt: null };
const page = { ...scope, cardVersion: 4, canEdit: true, items: [{ checklist, completed: 0, total: 0, percent: 0 }], nextCursor: null };
const ack = (title = 'New preparation') => ({ ...scope, changed: title !== checklist.title, cardVersion: title === checklist.title ? 4 : 5,
  checklist: { ...checklist, title, version: title === checklist.title ? 2 : 3 } });
const props = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false,
  onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn() });
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
async function review(title = 'New preparation') {
  fireEvent.click(screen.getByRole('button', { name: 'Rename a checklist' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Rename Preparations' }));
  fireEvent.change(screen.getByRole('textbox', { name: /Checklist title/ }), { target: { value: title } });
}
it('reviews server edit admission and writes scoped Card/Checklist revisions', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? ack() : page);
  const p = props(); render(<ChecklistManageControl {...p} />); await review(' New preparation ');
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist title' })); await screen.findByText('Checklist renamed.');
  const writes = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(writes).toHaveLength(1);
  expect(writes[0][0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}`);
  expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ title: 'New preparation', cardVersion: 4, version: 2 });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Rename a checklist' })).toHaveFocus());
  expect(p.onBusyChange).toHaveBeenLastCalledWith(false);
});
it('accepts a canonical unchanged rename without inventing revision changes', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? ack(checklist.title) : page);
  render(<ChecklistManageControl {...props()} />); await review(checklist.title);
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist title' }));
  expect(await screen.findByText('Checklist title is unchanged.')).toBeVisible();
});
it('retains original body/key, chosen child and actor after an unconfirmed rename and newer snapshot', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'PATCH') return page;
    if (++writes === 1) throw new WorkRequestError(0, null); return ack();
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await review();
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist title' })); const retry = await screen.findByRole('button', { name: 'Retry checklist rename' });
  await waitFor(() => expect(retry).toHaveFocus()); view.rerender(<ChecklistManageControl {...p} version={5} unavailable />);
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument(); view.rerender(<ChecklistManageControl {...p} version={5} />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry checklist rename' })).toHaveFocus());
  expect(screen.getByRole('textbox')).toBeDisabled(); fireEvent.click(screen.getByRole('button', { name: 'Retry checklist rename' }));
  await screen.findByText('Checklist renamed.');
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH');
  expect(calls).toHaveLength(2); expect(calls[1][0]).toBe(calls[0][0]); expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
  expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it.each([{ canEdit: false }, { cardVersion: 5 }, { boardId: id(90) }])('rejects stale, foreign or read-only selection without offering protected editing (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : { ...page, ...change });
  render(<ChecklistManageControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Rename a checklist' }));
  expect(await screen.findByText('Unable to review current checklists. Refresh the Card and try again.')).toBeVisible();
  expect(screen.queryByRole('button', { name: 'Rename Preparations' })).not.toBeInTheDocument();
});
it('preserves a dirty title and requires explicit latest review after a concurrent Card revision', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : page);
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await review(); view.rerender(<ChecklistManageControl {...p} version={5} />);
  expect(screen.getByRole('textbox')).toHaveValue('New preparation'); expect(screen.getByRole('button', { name: 'Save checklist title' })).toBeDisabled();
  expect(screen.getByRole('alert')).toHaveTextContent('title is preserved');
  fireEvent.click(screen.getByRole('button', { name: 'Discard checklist rename and load latest' }));
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument(); expect(p.onRefresh).toHaveBeenCalled();
});
it('requires current review after definite conflict and keeps the rejected draft', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method === 'PATCH') throw new WorkRequestError(409, null); return page;
  });
  const p = props(); render(<ChecklistManageControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Save checklist title' }));
  await screen.findByText(/This checklist rename is unavailable/); expect(screen.getByRole('textbox')).toHaveValue('New preparation');
  expect(screen.getByRole('textbox')).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Retry checklist rename' })).not.toBeInTheDocument();
  expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true);
});
it.each([{ cardVersion: 6 }, { checklist: { ...ack().checklist, id: id(90) } }, { checklist: { ...ack().checklist, rank: rank(2) } }, { changed: false }])('does not acknowledge foreign children or invented revision/order changes (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? { ...ack(), ...change } : page);
  render(<ChecklistManageControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Save checklist title' }));
  expect(await screen.findByRole('button', { name: 'Retry checklist rename' })).toBeEnabled(); expect(screen.queryByText('Checklist renamed.')).not.toBeInTheDocument();
});
it('paginates the review and retains full child revision for a later-page selection', async () => {
  const rows = Array.from({ length: 50 }, (_, n) => ({ ...page.items[0], checklist: { ...checklist, id: id(n + 20), rank: rank(n + 1), title: `Checklist ${n + 1}` } }));
  const cursor = `${scope.cardId}/${rank(50)}/${id(69)}`;
  const later = { ...checklist, id: id(70), rank: rank(51), title: 'Later checklist', version: 7 };
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : path.includes('?after=') ? { ...page, items: [{ ...page.items[0], checklist: later }] } : { ...page, items: rows, nextCursor: cursor });
  render(<ChecklistManageControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Rename a checklist' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Next checklists to rename' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Rename Later checklist' })); expect(screen.getByRole('textbox')).toHaveValue('Later checklist');
  expect(vi.mocked(workRequest).mock.calls.some(([path]) => path.endsWith(`?after=${encodeURIComponent(cursor)}`))).toBe(true);
});
it('prevents double submission and cancels in-flight commands when changing Cards', async () => {
  let read = 0; let resolve: (value: unknown) => void = () => {}; let signal: AbortSignal | undefined;
  vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return ++read < 3 ? profile : { ...profile, id: id(9) };
    if (options?.method === 'PATCH') { signal = options.signal as AbortSignal; return new Promise(yes => { resolve = yes; }); }
    return page;
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await review();
  const save = screen.getByRole('button', { name: 'Save checklist title' }); fireEvent.click(save); fireEvent.click(save);
  await waitFor(() => expect(vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH')).toHaveLength(1));
  view.rerender(<ChecklistManageControl {...p} cardId={id(90)} />); expect(signal?.aborted).toBe(true);
  await act(async () => resolve(ack())); expect(screen.queryByText('Checklist renamed.')).not.toBeInTheDocument();
});
it('blocks a changed actor before sending a reviewed rename', async () => {
  let profiles = 0; vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? { ...profile, id: ++profiles === 1 ? profile.id : id(9) } : page);
  render(<ChecklistManageControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Save checklist title' }));
  expect(await screen.findByText(/This checklist rename is unavailable/)).toBeVisible();
  expect(vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH')).toHaveLength(0);
  expect(screen.getByRole('textbox')).toHaveValue('New preparation');
});
