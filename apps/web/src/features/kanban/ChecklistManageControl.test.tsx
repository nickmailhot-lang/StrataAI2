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
it('owns item recovery through the manager and returns focus only after its original acknowledgment', async () => {
  const child = { id: id(5), organizationId: scope.organizationId, checklistId: checklist.id, text: 'Prepare', rank: rank(1),
    completed: false, completedBy: null, completedAt: null, createdAt: now, updatedAt: now, version: 3, deletedAt: null };
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile;
    if (options?.method === 'PATCH') {
      if (++writes === 1) throw new WorkRequestError(0, null);
      return { ...scope, cardVersion: 5, changed: true, checklist: { ...checklist, version: 3 },
        item: { ...child, version: 4, completed: true, completedAt: now, completedBy: profile.id } };
    }
    return path.endsWith('/items') ? { ...scope, cardVersion: 4, canEdit: true, summary: { checklist, total: 1, completed: 0, percent: 0 }, items: [child], nextCursor: null } : page;
  });
  const p = props(); render(<ChecklistManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Manage items in Preparations' }));
  fireEvent.click(screen.getByRole('button', { name: 'Review checklist items' })); fireEvent.click(await screen.findByRole('button', { name: 'Edit item: Prepare' }));
  fireEvent.click(screen.getByRole('checkbox', { name: 'Item complete' })); fireEvent.click(screen.getByRole('button', { name: 'Save checklist item' }));
  const retry = await screen.findByRole('button', { name: 'Retry checklist item change' });
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(true)); expect(screen.queryByRole('button', { name: 'Manage checklists' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByText('Checklist item saved.');
  await waitFor(() => expect(p.onRecoveryChange).toHaveBeenLastCalledWith(false));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Manage checklists' })).toHaveFocus());
});
const sibling = { ...checklist, id: id(6), title: 'Execution', rank: rank(10) };
const movePage = { ...page, items: [page.items[0], { ...page.items[0], checklist: sibling }] };
const moved = (changed = true) => ({ ...scope, changed, cardVersion: changed ? 5 : 4,
  checklist: { ...checklist, rank: changed ? rank(15) : checklist.rank, version: changed ? 3 : 2 } });
const itemAdded = (text = 'Prepare materials') => ({ ...scope, changed: true, cardVersion: 5, checklist: { ...checklist, version: 3 },
  item: { id: id(10), organizationId: scope.organizationId, checklistId: checklist.id, text, rank: rank(10), version: 1,
    createdAt: now, updatedAt: now, deletedAt: null, completed: false, completedBy: null, completedAt: null } });
async function reviewItem(text = 'Prepare materials') {
  fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Add item to Preparations' }));
  fireEvent.change(screen.getByRole('textbox', { name: /Checklist item text/ }), { target: { value: text } });
}
it('creates normalized multiline item text using the reviewed Card and Checklist revisions', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'POST' ? itemAdded('Prepare\nmaterials') : page);
  render(<ChecklistManageControl {...props()} />); await reviewItem(' Prepare\nmaterials '); fireEvent.click(screen.getByRole('button', { name: 'Create checklist item' }));
  await screen.findByText('Checklist item added.'); const writes = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'POST');
  expect(writes).toHaveLength(1); expect(writes[0][0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}/items`);
  expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ text: 'Prepare\nmaterials', cardVersion: 4, checklistVersion: 2 });
});
it.each(['x'.repeat(2001), 'text\0hidden'])('rejects invalid item text without issuing a command', async text => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : page);
  render(<ChecklistManageControl {...props()} />); await reviewItem(text); fireEvent.click(screen.getByRole('button', { name: 'Create checklist item' }));
  await screen.findByText('Enter item text of 1 to 2000 characters.'); expect(vi.mocked(workRequest).mock.calls.some(([, options]) => options?.method === 'POST')).toBe(false);
});
it('preserves item text and requires review after a concurrent Card revision or denied write', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method === 'POST') throw new WorkRequestError(403, null); return page;
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await reviewItem(); view.rerender(<ChecklistManageControl {...p} version={5} />);
  expect(screen.getByRole('textbox')).toHaveValue('Prepare materials'); expect(screen.getByRole('button', { name: 'Create checklist item' })).toBeDisabled();
  view.rerender(<ChecklistManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Create checklist item' }));
  await screen.findByText(/This item creation is unavailable/); expect(screen.getByRole('textbox')).toHaveValue('Prepare materials');
  expect(screen.getByRole('textbox')).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Retry checklist item creation' })).not.toBeInTheDocument();
});
it('recovers an unconfirmed item creation with exactly the same text, key, actor and revisions', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'POST') return page;
    if (++writes === 1) throw new WorkRequestError(0, null); return itemAdded();
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await reviewItem(); fireEvent.click(screen.getByRole('button', { name: 'Create checklist item' }));
  await screen.findByRole('button', { name: 'Retry checklist item creation' }); view.rerender(<ChecklistManageControl {...p} version={5} unavailable />);
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument(); view.rerender(<ChecklistManageControl {...p} version={5} />);
  const retry = screen.getByRole('button', { name: 'Retry checklist item creation' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.getByRole('textbox')).toBeDisabled(); fireEvent.click(retry); await screen.findByText('Checklist item added.');
  const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'POST'); expect(calls).toHaveLength(2);
  expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
});
it.each([{ cardVersion: 6 }, { checklist: { ...itemAdded().checklist, rank: rank(5) } },
  { item: { ...itemAdded().item, checklistId: id(90) } }, { item: { ...itemAdded().item, completed: true, completedAt: now, completedBy: profile.id } },
  { item: { ...itemAdded().item, version: 2 } }])('retains recovery after malformed or foreign item acknowledgments (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'POST' ? { ...itemAdded(), ...change } : page);
  render(<ChecklistManageControl {...props()} />); await reviewItem(); fireEvent.click(screen.getByRole('button', { name: 'Create checklist item' }));
  expect(await screen.findByRole('button', { name: 'Retry checklist item creation' })).toBeEnabled(); expect(screen.queryByText('Checklist item added.')).not.toBeInTheDocument();
});
async function reviewMove() {
  fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Move Preparations' }));
}
it('requires a destination, submits a scoped append, and accepts the new rank', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? moved() : movePage);
  render(<ChecklistManageControl {...props()} />); await reviewMove();
  const save = screen.getByRole('button', { name: 'Save checklist position' }); expect(save).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Place before Preparations' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Place at end' })); fireEvent.click(save);
  expect(await screen.findByText('Checklist moved.')).toBeVisible();
  const writes = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH');
  expect(writes).toHaveLength(1); expect(writes[0][0]).toBe(`/cards/${scope.cardId}/checklists/${checklist.id}/position`);
  expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ beforeId: null, cardVersion: 4, version: 2 });
});
it('accepts an already-before-sibling no-op with unchanged versions and timestamps', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? moved(false) : movePage);
  render(<ChecklistManageControl {...props()} />); await reviewMove();
  fireEvent.click(screen.getByRole('button', { name: 'Place before Execution' })); fireEvent.click(screen.getByRole('button', { name: 'Save checklist position' }));
  expect(await screen.findByText('Checklist position is unchanged.')).toBeVisible();
});
it('retains the reviewed destination and original key after an unconfirmed move and re-admission', async () => {
  let writes = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'PATCH') return movePage;
    if (++writes === 1) throw new WorkRequestError(0, null); return moved();
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await reviewMove();
  fireEvent.click(screen.getByRole('button', { name: 'Place at end' })); fireEvent.click(screen.getByRole('button', { name: 'Save checklist position' }));
  await screen.findByRole('button', { name: 'Retry checklist move' }); view.rerender(<ChecklistManageControl {...p} version={5} unavailable />);
  expect(screen.queryByText('Chosen position: at end')).not.toBeInTheDocument(); view.rerender(<ChecklistManageControl {...p} version={5} />);
  const retry = screen.getByRole('button', { name: 'Retry checklist move' }); await waitFor(() => expect(retry).toHaveFocus());
  expect(screen.queryByRole('button', { name: 'Place at end' })).not.toBeInTheDocument(); fireEvent.click(retry);
  await screen.findByText('Checklist moved.'); const calls = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'PATCH');
  expect(calls).toHaveLength(2); expect(calls[1][1]!.body).toBe(calls[0][1]!.body); expect(calls[1][1]!.headers).toEqual(calls[0][1]!.headers);
});
it.each([{ cardVersion: 6 }, { checklist: { ...moved().checklist, rank: rank(5) } }, { checklist: { ...moved().checklist, title: 'Invented' } }, { changed: false }])('retains recovery after an invalid position acknowledgment (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH' ? { ...moved(), ...change } : movePage);
  render(<ChecklistManageControl {...props()} />); await reviewMove(); fireEvent.click(screen.getByRole('button', { name: 'Place at end' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist position' })); expect(await screen.findByRole('button', { name: 'Retry checklist move' })).toBeEnabled();
  expect(screen.queryByText('Checklist moved.')).not.toBeInTheDocument();
});
it('chooses a before-anchor on a later bounded page without fetching every checklist', async () => {
  const rows = Array.from({ length: 50 }, (_, n) => ({ ...page.items[0], checklist: { ...checklist, id: n === 0 ? checklist.id : id(n + 20), rank: rank((n + 1) * 10), title: n === 0 ? checklist.title : `Checklist ${n + 1}` } }));
  const cursor = `${scope.cardId}/${rank(500)}/${id(69)}`;
  const anchor = { ...sibling, rank: rank(510), title: 'Last anchor' };
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'PATCH'
    ? { ...scope, cardVersion: 5, changed: true, checklist: { ...rows[0].checklist, rank: rank(505), version: 3 } }
    : path.includes('?after=') ? { ...page, items: [{ ...page.items[0], checklist: anchor }] } : { ...page, items: rows, nextCursor: cursor });
  render(<ChecklistManageControl {...props()} />); await reviewMove(); expect(screen.queryByRole('button', { name: 'Place at end' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Next position choices' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Place before Last anchor' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save checklist position' })); await screen.findByText('Checklist moved.');
  const reads = vi.mocked(workRequest).mock.calls.filter(([path]) => path !== '/me' && !path.endsWith('/position')); expect(reads).toHaveLength(2);
  const write = vi.mocked(workRequest).mock.calls.find(([, options]) => options?.method === 'PATCH');
  expect(JSON.parse(write![1]!.body as string)).toEqual({ beforeId: anchor.id, cardVersion: 4, version: 2 });
});
it('requires fresh review after a concurrent revision or rank-space conflict', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method === 'PATCH') throw new WorkRequestError(409, null); return movePage;
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} />); await reviewMove(); fireEvent.click(screen.getByRole('button', { name: 'Place at end' }));
  view.rerender(<ChecklistManageControl {...p} version={5} />); expect(screen.getByRole('button', { name: 'Save checklist position' })).toBeDisabled();
  view.rerender(<ChecklistManageControl {...p} />); fireEvent.click(screen.getByRole('button', { name: 'Save checklist position' }));
  await screen.findByText(/This checklist move is unavailable/); expect(screen.queryByRole('button', { name: 'Retry checklist move' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Discard checklist move and load latest' })).toBeEnabled();
});
async function review(title = 'New preparation') {
  fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
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
  await waitFor(() => expect(screen.getByRole('button', { name: 'Manage checklists' })).toHaveFocus());
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
  render(<ChecklistManageControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
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
  render(<ChecklistManageControl {...props()} />); fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Next checklists to manage' }));
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
const deletion = () => ({ ...scope, changed: true, cardVersion: 5, deletedItems: 63,
  checklist: { ...checklist, version: 3, deletedAt: now } });
const deletePage = { ...page, items: [{ ...page.items[0], total: 63 }] };
async function reviewDeletion() {
  fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Delete Preparations' }));
}
it('requires explicit confirmation and acknowledges the reviewed complete cascade count', async () => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'DELETE' ? deletion() : deletePage);
  const p = props(); render(<ChecklistManageControl {...p} canAdminister />); await reviewDeletion();
  expect(screen.getByRole('alert')).toHaveTextContent('63 active items');
  const save = screen.getByRole('button', { name: 'Delete confirmed checklist' }); expect(save).toBeDisabled();
  fireEvent.click(save); expect(vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'DELETE')).toHaveLength(0);
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm checklist deletion' })); fireEvent.click(save);
  expect(await screen.findByText('Checklist deleted.')).toBeVisible();
  const writes = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'DELETE'); expect(writes).toHaveLength(1);
  expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ confirmed: true, cardVersion: 4, version: 2 });
});
it('omits deletion for contributors and disables reviewed deletion after administration is lost', async () => {
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile : deletePage);
  const p = props(); const view = render(<ChecklistManageControl {...p} />);
  fireEvent.click(screen.getByRole('button', { name: 'Manage checklists' })); await screen.findByRole('button', { name: 'Rename Preparations' });
  expect(screen.queryByRole('button', { name: 'Delete Preparations' })).not.toBeInTheDocument();
  view.rerender(<ChecklistManageControl {...p} canAdminister />); fireEvent.click(screen.getByRole('button', { name: 'Delete Preparations' }));
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm checklist deletion' }));
  view.rerender(<ChecklistManageControl {...p} canAdminister={false} />);
  expect(screen.getByRole('button', { name: 'Delete confirmed checklist' })).toBeDisabled();
});
it('retains confirmed deletion input and retry key after the committed acknowledgment is lost', async () => {
  let writeCount = 0; vi.mocked(workRequest).mockImplementation(async (path, options) => {
    if (path === '/me') return profile; if (options?.method !== 'DELETE') return deletePage;
    if (++writeCount === 1) throw new WorkRequestError(0, null); return deletion();
  });
  const p = props(); const view = render(<ChecklistManageControl {...p} canAdminister />); await reviewDeletion();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm checklist deletion' })); fireEvent.click(screen.getByRole('button', { name: 'Delete confirmed checklist' }));
  const retry = await screen.findByRole('button', { name: 'Retry checklist deletion' }); await waitFor(() => expect(retry).toHaveFocus());
  view.rerender(<ChecklistManageControl {...p} canAdminister version={5} unavailable />); expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  view.rerender(<ChecklistManageControl {...p} canAdminister version={5} />);
  expect(screen.getByRole('checkbox')).toBeDisabled(); fireEvent.click(screen.getByRole('button', { name: 'Retry checklist deletion' })); await screen.findByText('Checklist deleted.');
  const writes = vi.mocked(workRequest).mock.calls.filter(([, options]) => options?.method === 'DELETE');
  expect(writes).toHaveLength(2); expect(writes[1][1]!.body).toBe(writes[0][1]!.body); expect(writes[1][1]!.headers).toEqual(writes[0][1]!.headers);
});
it.each([{ deletedItems: 62 }, { cardVersion: 6 }, { checklist: { ...deletion().checklist, deletedAt: null } },
  { checklist: { ...deletion().checklist, title: 'Foreign title' } }])('keeps noncanonical deletion acknowledgments unresolved (%j)', async change => {
  vi.mocked(workRequest).mockImplementation(async (path, options) => path === '/me' ? profile : options?.method === 'DELETE' ? { ...deletion(), ...change } : deletePage);
  render(<ChecklistManageControl {...props()} canAdminister />); await reviewDeletion();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Confirm checklist deletion' })); fireEvent.click(screen.getByRole('button', { name: 'Delete confirmed checklist' }));
  expect(await screen.findByRole('button', { name: 'Retry checklist deletion' })).toBeEnabled(); expect(screen.queryByText('Checklist deleted.')).not.toBeInTheDocument();
});
