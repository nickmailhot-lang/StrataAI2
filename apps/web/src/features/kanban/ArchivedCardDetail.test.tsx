import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ArchivedCardDetail, parseArchivedCardDetail } from './ArchivedCardDetail';
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(4), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const detail = { ...scope, title: 'Archived <script>🙂', description: 'Read-only notes', version: 2 };
const props = { ...scope, unavailable: false, refreshSequence: '1', reconnectSequence: 0, onDenied: vi.fn(), onRefresh: vi.fn() };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => vi.unstubAllGlobals());
it('offers explicit classification of an admitted archived Card independently from operational editing', async () => {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => String(input) === '/me' ? reply(profile) : reply(detail)));
  const ui = render(<MemoryRouter><ArchivedCardDetail {...props} canAdminister /></MemoryRouter>);
  await screen.findByText(detail.title);
  expect(screen.getByRole('button', { name: 'Retention classification for this Card' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Review archived attachments' })).toBeEnabled();
  expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  ui.rerender(<MemoryRouter><ArchivedCardDetail {...props} canAdminister={false} /></MemoryRouter>);
  expect(screen.queryByRole('button', { name: 'Retention classification for this Card' })).not.toBeInTheDocument();
});
it('keeps retained checklists and attachments readable without child mutation controls while an archived parent is admitted', async () => {
  const at = '2026-10-04T08:00:00.123456Z'; const rank = '000000000000000000000000001000';
  const checklist = { id: id(5), organizationId: scope.organizationId, cardId: scope.cardId, title: 'Retained preparations',
    rank, version: 1, createdAt: at, updatedAt: at, deletedAt: null };
  const summary = { checklist, total: 1, completed: 0, percent: 0 };
  const item = { id: id(6), organizationId: scope.organizationId, checklistId: checklist.id, text: 'Retained preparation',
    rank, completed: false, completedAt: null, completedBy: null, createdAt: at, updatedAt: at, version: 1, deletedAt: null };
  const attachment = { id: id(7), organizationId: scope.organizationId, cardId: scope.cardId, uploaderId: profile.id,
    kind: 1, displayName: 'Retained reference', mimeType: null, sizeBytes: null, url: 'https://example.test/retained', scanStatus: 0,
    scannedAt: null, createdAt: at, updatedAt: at, version: 1, deletedAt: null, lifecycleState: 0, archivedAt: null, deletedBy: null };
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path === '/me') return reply(profile);
    if (path.endsWith('/archived-details')) return reply(detail);
    if (path.endsWith('/checklists')) return reply({ ...scope, cardVersion: 2, canEdit: false, items: [summary], nextCursor: null });
    if (path.endsWith('/items')) return reply({ ...scope, cardVersion: 2, canEdit: false, summary, items: [item], nextCursor: null });
    return reply({ ...scope, cardVersion: 2, canEdit: false, items: [attachment], nextCursor: null });
  }));
  const view = render(<MemoryRouter><ArchivedCardDetail {...props} /></MemoryRouter>);
  await screen.findByText(detail.title);
  fireEvent.click(screen.getByRole('button', { name: 'Show checklists' })); await screen.findByText('Read-only checklists.');
  fireEvent.click(screen.getByRole('button', { name: 'Show items in Retained preparations' }));
  await screen.findByText('Incomplete: Retained preparation');
  fireEvent.click(screen.getByRole('button', { name: 'Show attachments' }));
  expect(await screen.findByRole('link', { name: 'Retained reference (opens in a new tab)' })).toHaveAttribute('href', attachment.url);
  for (const name of ['Add checklist', 'Manage checklists', 'Manage attachments', 'Add link attachment', 'Add file attachment'])
    expect(screen.queryByRole('button', { name })).toBeNull();
  expect(screen.queryByRole('textbox')).toBeNull(); expect(screen.queryByRole('checkbox')).toBeNull();
  view.rerender(<MemoryRouter><ArchivedCardDetail {...props} unavailable refreshSequence="2" /></MemoryRouter>);
  expect(screen.queryByText('Incomplete: Retained preparation')).toBeNull(); expect(screen.queryByRole('link')).toBeNull();
});
it('reads scoped archived details, exposes comments and history without mutation controls, and purges immediately on invalidation', async () => {
  const requests: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input); requests.push(path);
    if (path === '/me') return reply(profile);
    if (path.endsWith('/archived-details')) return reply(detail);
    if (path.endsWith('/activity')) return reply({ ...scope, kind: 'CARD', targetId: scope.cardId, items: [], nextCursor: null });
    return reply({ ...scope, cardVersion: 2, items: [], nextCursor: null, canComment: false });
  }));
  const view = render(<MemoryRouter><ArchivedCardDetail {...props} /></MemoryRouter>);
  expect(await screen.findByText(detail.title)).toBeVisible(); expect(screen.queryByRole('textbox')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await waitFor(() => expect(requests.some(path => path.endsWith('/activity'))).toBe(true));
  expect(screen.getByRole('button', { name: 'Review Card comments' })).toBeEnabled();
  fireEvent.click(screen.getByRole('button', { name: 'Review Card comments' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Add comment' })).toBeDisabled());
  view.rerender(<MemoryRouter><ArchivedCardDetail {...props} unavailable refreshSequence="2" /></MemoryRouter>);
  expect(screen.queryByText(detail.title)).toBeNull(); expect(screen.queryByRole('button', { name: 'Review Card activity' })).toBeNull();
});
it('fails closed for a mismatched scope and a changed authenticated account', async () => {
  expect(() => parseArchivedCardDetail({ ...detail, boardId: id(9) }, scope)).toThrow();
  let profiles = 0; const denied = vi.fn();
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => String(input) === '/me'
    ? reply({ ...profile, id: ++profiles === 1 ? profile.id : id(5) }) : reply(detail)));
  render(<MemoryRouter><ArchivedCardDetail {...props} onDenied={denied} /></MemoryRouter>);
  await waitFor(() => expect(denied).toHaveBeenCalled()); expect(screen.queryByText(detail.title)).toBeNull();
});

function retainedChecklistFixture(holdFirstItems = false) {
  const at = '2026-10-04T08:00:00.123456Z', rank = '000000000000000000000000001000';
  let currentScope = scope, currentActor = profile.id, itemReads = 0, catalogVersion = 2;
  let release!: (value: Response) => void; let firstSignal: AbortSignal | undefined;
  const checklist = { id: id(5), organizationId: scope.organizationId, cardId: scope.cardId, title: 'Retained preparations',
    rank, version: 1, createdAt: at, updatedAt: at, deletedAt: null };
  const item = { id: id(6), organizationId: scope.organizationId, checklistId: checklist.id, text: 'Retained preparation',
    rank, completed: false, completedAt: null, completedBy: null, createdAt: at, updatedAt: at, version: 1, deletedAt: null };
  const items = () => ({ ...currentScope, cardVersion: catalogVersion, canEdit: false,
    summary: { checklist: { ...checklist, cardId: currentScope.cardId }, total: 1, completed: 0, percent: 0 }, items: [item], nextCursor: null });
  const fetch = vi.fn(async (input: RequestInfo | URL, options?: RequestInit) => {
    const path = String(input);
    if (path === '/me') return reply({ ...profile, id: currentActor });
    if (path.endsWith('/archived-details')) return reply({ ...detail, ...currentScope });
    if (path.endsWith('/checklists')) return reply({ ...currentScope, cardVersion: catalogVersion, canEdit: false, items: [items().summary], nextCursor: null });
    if (path.endsWith('/items')) {
      itemReads++; if (holdFirstItems && itemReads === 1) {
        firstSignal = options?.signal as AbortSignal;
        return new Promise<Response>(resolve => { release = resolve; });
      }
      return reply(items());
    }
    throw new Error('Unexpected retained Checklist request');
  });
  vi.stubGlobal('fetch', fetch);
  return { fetch, itemReads: () => itemReads, firstSignal: () => firstSignal,
    releaseLate: () => release(reply({ ...items(), items: [{ ...item, text: 'Stale unpublished preparation' }] })),
    mismatchVersion: () => { catalogVersion = 3; }, changeActor: () => { currentActor = id(40); }, changeCard: () => { currentScope = { ...scope, cardId: id(30) }; },
    element: (extra = {}) => <MemoryRouter><ArchivedCardDetail {...props} {...currentScope} {...extra} /></MemoryRouter> };
}

for (const pending of [false, true]) for (const invalidation of ['access', 'sequence']) {
  it(`recovers the open retained Checklist after ${invalidation} revalidation (pending items=${pending})`, async () => {
    const f = retainedChecklistFixture(pending); const view = render(f.element());
    await screen.findByText(detail.title);
    fireEvent.click(screen.getByRole('button', { name: 'Show checklists' })); await screen.findByText('Read-only checklists.');
    fireEvent.click(screen.getByRole('button', { name: 'Show items in Retained preparations' }));
    if (pending) await waitFor(() => expect(f.itemReads()).toBe(1)); else await screen.findByText('Incomplete: Retained preparation');
    view.rerender(f.element({ unavailable: invalidation === 'access', refreshSequence: '2' }));
    expect(screen.queryByText('Incomplete: Retained preparation')).toBeNull();
    if (pending) {
      expect(f.firstSignal()?.aborted).toBe(true); await act(async () => f.releaseLate());
      expect(screen.queryByText('Incomplete: Stale unpublished preparation')).toBeNull();
    }
    if (invalidation === 'access') view.rerender(f.element({ unavailable: false, refreshSequence: '2' }));
    expect(await screen.findByText('Incomplete: Retained preparation')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Hide checklists' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'Hide items in Retained preparations' })).toHaveAttribute('aria-expanded', 'true');
    expect(f.itemReads()).toBe(2);
    expect(f.fetch.mock.calls.every(call => !call[1]?.method || call[1]?.method === 'GET')).toBe(true);
    expect(screen.queryByRole('textbox')).toBeNull(); expect(screen.queryByRole('checkbox')).toBeNull();
  });
}
for (const change of ['actor', 'Card']) {
  it(`retires retained Checklist disclosure choices for a different ${change}`, async () => {
    const f = retainedChecklistFixture(); const view = render(f.element());
    await screen.findByText(detail.title);
    fireEvent.click(screen.getByRole('button', { name: 'Show checklists' })); await screen.findByText('Read-only checklists.');
    fireEvent.click(screen.getByRole('button', { name: 'Show items in Retained preparations' })); await screen.findByText('Incomplete: Retained preparation');
    if (change === 'actor') f.changeActor(); else f.changeCard();
    view.rerender(f.element({ refreshSequence: '2' })); await screen.findByText(detail.title);
    expect(screen.getByRole('button', { name: 'Show checklists' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('Incomplete: Retained preparation')).toBeNull(); expect(f.itemReads()).toBe(1);
  });
}

it('bounds archived Checklist revision re-admission across reader resets and retires the guard for another actor', async () => {
  const f = retainedChecklistFixture(); const refresh = vi.fn(); const view = render(f.element({ onRefresh: refresh }));
  await screen.findByText(detail.title);
  fireEvent.click(screen.getByRole('button', { name: 'Show checklists' })); await screen.findByText('Read-only checklists.');
  fireEvent.click(screen.getByRole('button', { name: 'Show items in Retained preparations' })); await screen.findByText('Incomplete: Retained preparation');
  f.mismatchVersion(); view.rerender(f.element({ onRefresh: refresh, refreshSequence: '2' }));
  await screen.findByText(/Unable to load current checklists/); expect(refresh).toHaveBeenCalledOnce();
  expect(screen.queryByText('Incomplete: Retained preparation')).toBeNull();
  view.rerender(f.element({ onRefresh: refresh, refreshSequence: '3' }));
  await screen.findByText(/Unable to load current checklists/); expect(refresh).toHaveBeenCalledOnce();
  f.changeActor(); view.rerender(f.element({ onRefresh: refresh, refreshSequence: '4' })); await screen.findByText(detail.title);
  expect(screen.getByRole('button', { name: 'Show checklists' })).toHaveAttribute('aria-expanded', 'false');
  fireEvent.click(screen.getByRole('button', { name: 'Show checklists' }));
  await screen.findByText(/Unable to load current checklists/); expect(refresh).toHaveBeenCalledTimes(2);
});
