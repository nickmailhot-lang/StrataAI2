import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ChecklistItemManageControl } from './ChecklistItemManageControl';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async importOriginal => ({ ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const now = '2026-10-03T01:00:00.123456Z'; const later = '2026-10-03T01:00:01.123456Z'; const rank = (n: number) => String(n).padStart(30, '0');
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const checklist = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, title: 'Preparations', rank: rank(1),
  createdAt: now, updatedAt: now, version: 2, deletedAt: null };
const item = { id: id(5), organizationId: scope.organizationId, checklistId: checklist.id, text: 'Prepare', rank: rank(1),
  completed: false, completedBy: null, completedAt: null, createdAt: now, updatedAt: now, version: 3, deletedAt: null };
const page = { ...scope, cardVersion: 4, canEdit: true, summary: { checklist, total: 1, completed: 0, percent: 0 }, items: [item], nextCursor: null };
const ack = (text = 'Prepare', completed = true) => {
  const changed = text !== item.text || completed !== item.completed;
  return { ...scope, changed, cardVersion: 4 + Number(changed), checklist: { ...checklist, version: 2 + Number(changed), updatedAt: changed ? later : now },
    item: { ...item, text, completed, completedBy: completed ? profile.id : null, completedAt: completed ? later : null, updatedAt: changed ? later : now, version: 3 + Number(changed) } };
};
const props = () => ({ ...scope, checklist, actor: profile.id, version: 4, editable: true, disabled: false, unavailable: false,
  onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onClose: vi.fn() });
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
const sibling = { ...item, id: id(6), text: 'Execute', rank: rank(10) };
const movePage = { ...page, summary: { ...page.summary, total: 2 }, items: [item, sibling] };
const positioned = (changed = true) => ({ ...scope, changed, cardVersion: changed ? 5 : 4,
  checklist: { ...checklist, version: changed ? 3 : 2, updatedAt: changed ? later : now },
  item: { ...item, rank: changed ? rank(15) : item.rank, version: changed ? 4 : 3, updatedAt: changed ? later : now } });
async function reviewMove() {
  fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Move item: Prepare' }));
}
it('requires a reviewed destination and appends with exact Card/Checklist/item revisions', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? positioned() : movePage);
  const p = props(); render(<ChecklistItemManageControl {...p} />); await reviewMove();
  const save = screen.getByRole('button', { name: 'Save item position' }); expect(save).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Place before item: Prepare' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Place item at end' })); fireEvent.click(save);
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item moved.'));
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(calls).toHaveLength(1);
  expect(calls[0][0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}/items/${item.id}/position`);
  expect(JSON.parse(calls[0][1]!.body as string)).toEqual({ beforeId: null, cardVersion: 4, checklistVersion: 2, version: 3 });
});
it('accepts an already-before-sibling no-op without changing aggregate history', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? positioned(false) : movePage);
  const p = props(); render(<ChecklistItemManageControl {...p} />); await reviewMove(); fireEvent.click(screen.getByRole('button', { name: 'Place before item: Execute' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save item position' })); await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item position is unchanged.'));
});
it('preserves completion attribution and time when moving a completed item', async () => {
  const completed = { ...item, completed: true, completedBy: id(9), completedAt: now };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH'
    ? { ...positioned(), item: { ...positioned().item, completed: true, completedBy: id(9), completedAt: now } }
    : { ...movePage, summary: { ...movePage.summary, completed: 1, percent: 50 }, items: [completed, sibling] });
  const p = props(); render(<ChecklistItemManageControl {...p} />); await reviewMove(); fireEvent.click(screen.getByRole('button', { name: 'Place item at end' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save item position' })); await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item moved.'));
});
it('retains the original move destination, body and key through an unconfirmed response and newer snapshot', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'PATCH') return movePage;
    if (++writes === 1) throw new WorkRequestError(0, null); return positioned();
  });
  const p = props(); const view = render(<ChecklistItemManageControl {...p} />); await reviewMove(); fireEvent.click(screen.getByRole('button', { name: 'Place item at end' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save item position' })); await screen.findByRole('button', { name: 'Retry checklist item move' });
  view.rerender(<ChecklistItemManageControl {...p} version={5} unavailable />); expect(screen.queryByText('Chosen position: at end')).not.toBeInTheDocument();
  view.rerender(<ChecklistItemManageControl {...p} version={5} />); const retry = screen.getByRole('button', { name: 'Retry checklist item move' });
  await waitFor(() => expect(retry).toHaveFocus()); expect(screen.queryByRole('button', { name: 'Place item at end' })).not.toBeInTheDocument(); fireEvent.click(retry);
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item moved.')); const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH');
  expect(calls).toHaveLength(2); expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
});
it.each([{ cardVersion: 6 }, { item: { ...positioned().item, rank: rank(5) } }, { item: { ...positioned().item, text: 'Invented' } }, { changed: false }])('keeps recovery after a malformed position acknowledgment (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? { ...positioned(), ...change } : movePage);
  const p = props(); render(<ChecklistItemManageControl {...p} />); await reviewMove(); fireEvent.click(screen.getByRole('button', { name: 'Place item at end' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save item position' })); expect(await screen.findByRole('button', { name: 'Retry checklist item move' })).toBeEnabled(); expect(p.onClose).not.toHaveBeenCalled();
});
it('chooses a before-item anchor on a later bounded page', async () => {
  const rows = Array.from({ length: 50 }, (_, n) => ({ ...item, id: n === 0 ? item.id : id(n + 20), rank: rank((n + 1) * 10), text: n === 0 ? item.text : `Item ${n + 1}` }));
  const cursor = `${checklist.id}/${rank(500)}/${id(69)}`; const anchor = { ...sibling, rank: rank(510), text: 'Last anchor' };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH'
    ? { ...positioned(), item: { ...positioned().item, rank: rank(505) } }
    : { ...page, summary: { ...page.summary, total: 63 }, items: path.includes('?after=') ? [anchor] : rows, nextCursor: path.includes('?after=') ? null : cursor });
  const p = props(); render(<ChecklistItemManageControl {...p} />); await reviewMove(); expect(screen.queryByRole('button', { name: 'Place item at end' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Next item position choices' })); fireEvent.click(await screen.findByRole('button', { name: 'Place before item: Last anchor' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save item position' })); await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item moved.'));
  const call = vi.mocked(workRequest).mock.calls.find(([, options]) => options?.method === 'PATCH');
  expect(JSON.parse(call![1]!.body as string)).toEqual({ beforeId: anchor.id, cardVersion: 4, checklistVersion: 2, version: 3 });
  expect(vi.mocked(workRequest).mock.calls.filter(([path]) => path !== '/me' && !path.endsWith('/position'))).toHaveLength(2);
});
const deleted = () => ({ ...scope, changed: true, cardVersion: 5, checklist: { ...checklist, version: 3, updatedAt: later },
  item: { ...item, version: 4, updatedAt: later, deletedAt: later } });
async function reviewDeletion() {
  fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Delete item: Prepare' }));
}
it('requires administrator confirmation and accepts a completed tombstone with original history', async () => {
  const completed = { ...item, completed: true, completedBy: id(9), completedAt: now };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'DELETE'
    ? { ...deleted(), item: { ...deleted().item, completed: true, completedBy: id(9), completedAt: now } }
    : { ...page, items: [completed], summary: { ...page.summary, completed: 1, percent: 100 } });
  const p = props(); render(<ChecklistItemManageControl {...p} canAdminister />); await reviewDeletion();
  const save = screen.getByRole('button', { name: 'Delete confirmed item' }); expect(save).toBeDisabled(); fireEvent.click(save);
  expect(vi.mocked(workRequest).mock.calls.some(([, options]) => options?.method === 'DELETE')).toBe(false);
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm item deletion' })); fireEvent.click(save);
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item deleted.'));
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'DELETE'); expect(calls).toHaveLength(1);
  expect(JSON.parse(calls[0][1]!.body as string)).toEqual({ confirmed: true, cardVersion: 4, checklistVersion: 2, version: 3 });
});
it('omits deletion for contributors and disables a confirmed draft after administrator access is lost', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : page);
  const p = props(); const view = render(<ChecklistItemManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' }));
  await screen.findByRole('button', { name: 'Edit item: Prepare' }); expect(screen.queryByRole('button', { name: 'Delete item: Prepare' })).not.toBeInTheDocument();
  view.rerender(<ChecklistItemManageControl {...p} canAdminister />); fireEvent.click(screen.getByRole('button', { name: 'Delete item: Prepare' }));
  fireEvent.click(screen.getByRole('checkbox')); view.rerender(<ChecklistItemManageControl {...p} canAdminister={false} />);
  expect(screen.getByRole('button', { name: 'Delete confirmed item' })).toBeDisabled(); expect(screen.getByRole('checkbox')).toBeDisabled();
});
it('preserves exact confirmation/body/key through lost deletion acknowledgment and access re-admission', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'DELETE') return page;
    if (++writes === 1) throw new WorkRequestError(0, null); return deleted();
  });
  const p = props(); const view = render(<ChecklistItemManageControl {...p} canAdminister />); await reviewDeletion(); fireEvent.click(screen.getByRole('checkbox'));
  fireEvent.click(screen.getByRole('button', { name: 'Delete confirmed item' })); await screen.findByRole('button', { name: 'Retry checklist item deletion' });
  view.rerender(<ChecklistItemManageControl {...p} version={5} canAdminister unavailable />); expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  view.rerender(<ChecklistItemManageControl {...p} version={5} canAdminister={false} />); expect(screen.getByRole('button', { name: 'Retry checklist item deletion' })).toBeDisabled();
  view.rerender(<ChecklistItemManageControl {...p} version={5} canAdminister />); const retry = screen.getByRole('button', { name: 'Retry checklist item deletion' });
  await waitFor(() => expect(retry).toHaveFocus()); fireEvent.click(retry); await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item deleted.'));
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'DELETE'); expect(calls).toHaveLength(2);
  expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
});
it.each([{ cardVersion: 6 }, { item: { ...deleted().item, deletedAt: null } }, { item: { ...deleted().item, text: 'Invented' } },
  { checklist: { ...deleted().checklist, version: 2 } }])('retains deletion recovery after malformed acknowledgment (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'DELETE' ? { ...deleted(), ...change } : page);
  const p = props(); render(<ChecklistItemManageControl {...p} canAdminister />); await reviewDeletion(); fireEvent.click(screen.getByRole('checkbox'));
  fireEvent.click(screen.getByRole('button', { name: 'Delete confirmed item' })); expect(await screen.findByRole('button', { name: 'Retry checklist item deletion' })).toBeEnabled();
  expect(p.onClose).not.toHaveBeenCalled();
});
it('reviews a later bounded item page and retains that item revision in the command', async () => {
  const rows = Array.from({ length: 50 }, (_, n) => ({ ...item, id: id(n + 20), rank: rank(n + 1), text: `Item ${n + 1}` }));
  const cursor = `${checklist.id}/${rank(50)}/${id(69)}`;
  const selected = { ...item, id: id(70), rank: rank(51), text: 'Later item', version: 9 };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH'
    ? { ...ack(), item: { ...selected, completed: true, completedAt: later, completedBy: profile.id, updatedAt: later, version: 10 } }
    : { ...page, summary: { ...page.summary, total: 63 }, items: path.includes('?after=') ? [selected] : rows, nextCursor: path.includes('?after=') ? null : cursor });
  const p = props(); render(<ChecklistItemManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Next items to manage' })); fireEvent.click(await screen.findByRole('button', { name: 'Edit item: Later item' }));
  fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item saved.'));
  const call = vi.mocked(workRequest).mock.calls.find(([, options]) => options?.method === 'PATCH');
  expect(call![0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}/items/${selected.id}`);
  expect(JSON.parse(call![1]!.body as string)).toEqual({ text: 'Later item', completed: true, cardVersion: 4, checklistVersion: 2, version: 9 });
  expect(vi.mocked(workRequest).mock.calls.filter(([path]) => path !== '/me' && !path.endsWith(selected.id))).toHaveLength(2);
});
async function review() {
  fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Edit item: Prepare' }));
}
it('reviews canonical item and parent revisions and submits completion with the original actor', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? ack() : page);
  const p = props(); render(<ChecklistItemManageControl {...p} />); await review();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Item complete' })); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item saved.'));
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(calls).toHaveLength(1);
  expect(calls[0][0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}/items/${item.id}`);
  expect(JSON.parse(calls[0][1]!.body as string)).toEqual({ text: 'Prepare', completed: true, cardVersion: 4, checklistVersion: 2, version: 3 });
});
it('accepts an unchanged item without advancing aggregate revisions', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? ack('Prepare', false) : page);
  const p = props(); render(<ChecklistItemManageControl {...p} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item is unchanged.'));
});
it('preserves the first completer and time when editing text on a completed item', async () => {
  const completed = { ...item, completed: true, completedBy: id(9), completedAt: now };
  const result = { ...ack('Prepared carefully'), item: { ...ack('Prepared carefully').item, completedBy: id(9), completedAt: now } };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? result
    : { ...page, items: [completed], summary: { ...page.summary, completed: 1, percent: 100 } });
  const p = props(); render(<ChecklistItemManageControl {...p} />); await review();
  fireEvent.change(screen.getByRole('textbox'), { target: { value: ' Prepared carefully ' } }); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item saved.'));
});
it('clears completion attribution when uncompleting an item', async () => {
  const completed = { ...item, completed: true, completedBy: id(9), completedAt: now };
  const result = { ...ack('Prepare', false), changed: true, cardVersion: 5, checklist: { ...checklist, version: 3, updatedAt: later }, item: { ...item, version: 4, updatedAt: later } };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? result
    : { ...page, items: [completed], summary: { ...page.summary, completed: 1, percent: 100 } });
  const p = props(); render(<ChecklistItemManageControl {...p} />); await review(); fireEvent.click(screen.getByRole('checkbox', { name: 'Item complete' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' })); await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item saved.'));
});
it('retains the original text, completion, revisions and key after a lost acknowledgment and newer snapshot', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'PATCH') return page;
    if (++writes === 1) throw new WorkRequestError(0, null); return ack('Prepared carefully');
  });
  const p = props(); const view = render(<ChecklistItemManageControl {...p} />); await review();
  fireEvent.change(screen.getByRole('textbox'), { target: { value: ' Prepared carefully ' } }); fireEvent.click(screen.getByRole('checkbox', { name: 'Item complete' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' })); await screen.findByRole('button', { name: 'Retry checklist item change' });
  view.rerender(<ChecklistItemManageControl {...p} version={5} unavailable />); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  view.rerender(<ChecklistItemManageControl {...p} version={5} />); const retry = screen.getByRole('button', { name: 'Retry checklist item change' });
  await waitFor(() => expect(retry).toHaveFocus()); expect(screen.getByRole('textbox')).toBeDisabled(); expect(screen.getByRole('checkbox')).toBeChecked(); fireEvent.click(retry);
  await waitFor(() => expect(p.onClose).toHaveBeenCalledWith('Checklist item saved.'));
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(calls).toHaveLength(2);
  expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
});
it.each([{ cardVersion: 6 }, { checklist: { ...ack().checklist, rank: rank(2) } }, { item: { ...ack().item, rank: rank(2) } },
  { item: { ...ack().item, completedBy: id(9) } }, { item: { ...ack().item, completedAt: now } }, { item: { ...ack().item, checklistId: id(90) } }])('rejects invalid acknowledgments and keeps the original intent (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? { ...ack(), ...change } : page);
  const p = props(); render(<ChecklistItemManageControl {...p} />); await review(); fireEvent.click(screen.getByRole('checkbox'));
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' })); expect(await screen.findByRole('button', { name: 'Retry checklist item change' })).toBeEnabled();
  expect(p.onClose).not.toHaveBeenCalled();
});
it('preserves a dirty draft but prevents a new command after concurrent revision or definite conflict', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method === 'PATCH') throw new WorkRequestError(409, null); return page;
  });
  const p = props(); const view = render(<ChecklistItemManageControl {...p} />); await review();
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Prepared carefully' } }); fireEvent.click(screen.getByRole('checkbox'));
  view.rerender(<ChecklistItemManageControl {...p} version={5} />); expect(screen.getByRole('button', { name: 'Save checklist item' })).toBeDisabled();
  expect(screen.getByRole('textbox')).toHaveValue('Prepared carefully'); expect(screen.getByRole('checkbox')).toBeChecked();
  view.rerender(<ChecklistItemManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  await screen.findByText(/This item change is unavailable/); expect(screen.getByRole('textbox')).toHaveValue('Prepared carefully');
  expect(screen.getByRole('textbox')).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Retry checklist item change' })).not.toBeInTheDocument();
});
it('blocks a changed actor before a reviewed item update is sent', async () => {
  let profiles = 0; vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? { ...profile, id: ++profiles === 1 ? profile.id : id(9) } : page);
  render(<ChecklistItemManageControl {...props()} />); await review(); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  await screen.findByText(/This item change is unavailable/); expect(vi.mocked(workRequest).mock.calls.some(([, options]) => options?.method === 'PATCH')).toBe(false);
});
it.each([{ canEdit: false }, { cardVersion: 5 }, { checklist: { ...checklist, version: 3 } }])('rejects a stale or read-only review (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : { ...page, ...change, summary: { ...page.summary, checklist: 'checklist' in change ? change.checklist : checklist } });
  render(<ChecklistItemManageControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' }));
  await screen.findByText(/Unable to review current checklist items/); expect(screen.queryByRole('button', { name: 'Edit item: Prepare' })).not.toBeInTheDocument();
});
