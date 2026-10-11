import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SourceRetentionEntry, type SourceRetentionEntryProps } from './SourceRetentionEntry';
import { RetentionSourceClassificationIntent, type RetentionSourceReview } from './retentionSourceClient';
import { restoreRetentionSourceClassification, retainRetentionSourceClassification } from './retentionSourceRecovery';

const live = vi.hoisted(() => ({ invalidate: undefined as (() => void) | undefined }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn((options: { invalidate: () => void }) => {
  live.invalidate = options.invalidate; return () => {};
}) }));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const card = '33333333-3333-4333-8333-333333333333', board = '44444444-4444-4444-8444-444444444444';
const list = '55555555-5555-4555-8555-555555555555', other = '66666666-6666-4666-8666-666666666666';
const initial: RetentionSourceReview = { organizationId: org, recordId: card, kind: 'card', parentCardId: null,
  cardId: card, boardId: board, listId: list, cardVersion: 3, sourceVersion: 3, listVersion: 2,
  cardState: 'active', sourceState: 'active', listState: 'active', createdAt: '2026-10-11T12:00:00Z' };
const props = (): SourceRetentionEntryProps => ({ organizationId: org, boardId: board, cardId: card, cardVersion: 3,
  recordId: card, sourceVersion: 3, kind: 'card', canAdminister: true, unavailable: false, onRefresh: vi.fn() });
type Server = { organizationDenied?: boolean; boardDenied?: boolean; temporaryFailure?: boolean; profile?: string };
function transport(source = initial, server: Server = {}) {
  const fetch = vi.fn(async (path: string, options: RequestInit = {}) => {
    if (path === '/me') return new Response(JSON.stringify({ id: server.profile ?? actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }));
    if (path.includes('retention-policies')) return server.organizationDenied ? new Response('{}', { status: 404 })
      : new Response(JSON.stringify({ organizationId: org, items: [], nextAfter: null }));
    if (path === `/boards/${board}`) return server.temporaryFailure ? new Response('{}', { status: 503 })
      : new Response(JSON.stringify({ board: { id: board, organizationId: org, lifecycleState: 'active' },
        access: { canView: true, canAdminister: !server.boardDenied } }));
    if (path.endsWith('/review')) return new Response(JSON.stringify(source));
    if (options.method === 'POST') {
      const request = JSON.parse(options.body as string) as { category: string; jurisdiction: string | null };
      return new Response(JSON.stringify({ reference: { organizationId: org, recordClass: source.kind === 'attachment' ? 'document' : 'operationalWork',
        recordId: source.recordId, kind: source.kind, parentCardId: source.parentCardId, contextVersion: 1, originalBoardId: board,
        sourceVersion: source.sourceVersion }, category: request.category, jurisdiction: request.jurisdiction,
        actorId: actor, eventId: other, registeredAt: '2026-10-11T12:00:01Z' }));
    }
    throw new Error('Unexpected transport');
  });
  vi.stubGlobal('fetch', fetch); return fetch;
}
async function open(label = 'Card') {
  fireEvent.click(screen.getByRole('button', { name: `Retention classification for this ${label}` }));
  await screen.findByRole('button', { name: 'Review source classification' });
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); live.invalidate = undefined; });
describe('PRD-70 actual source entry admission and withdrawal', () => {
  it('clears Organization recovery on confirmed role withdrawal even before the disclosure is opened', async () => {
    const intent = RetentionSourceClassificationIntent.review(actor, { ...initial, recordId: other, cardId: other }, 'private.other', null, true);
    retainRetentionSourceClassification(sessionStorage, intent);
    const fetch = transport(initial, { organizationDenied: true });
    render(<SourceRetentionEntry {...props()} canAdminister={false} />);
    await waitFor(() => expect(restoreRetentionSourceClassification(sessionStorage, actor, org)).toBeUndefined());
    expect(fetch.mock.calls.some(([path]) => path.includes('retention-sources'))).toBe(false);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
  it.each([true, false])('clears only this Card family after Board withdrawal (matching family: %s)', async matching => {
    const source: RetentionSourceReview = matching
      ? { ...initial, kind: 'cardComment', recordId: other, parentCardId: card, sourceVersion: 2 }
      : { ...initial, recordId: other, cardId: other };
    const intent = RetentionSourceClassificationIntent.review(actor, source, 'private.original', null, true);
    retainRetentionSourceClassification(sessionStorage, intent);
    const fetch = transport(); render(<SourceRetentionEntry {...props()} canAdminister={false} />);
    await waitFor(() => expect(fetch.mock.calls.some(([path]) => path.includes('retention-policies'))).toBe(true));
    await act(async () => {});
    expect(restoreRetentionSourceClassification(sessionStorage, actor, org)?.key).toBe(matching ? undefined : intent.key);
    expect(fetch.mock.calls.some(([path]) => path.includes('retention-sources'))).toBe(false);
  });
  it('preserves original recovery while the parent permission snapshot is uncertain', async () => {
    const intent = RetentionSourceClassificationIntent.review(actor, initial, 'private.original', null, true);
    retainRetentionSourceClassification(sessionStorage, intent);
    const fetch = transport(initial, { organizationDenied: true }), context = props();
    const ui = render(<SourceRetentionEntry {...context} canAdminister={false} unavailable />);
    await act(async () => {});
    expect(fetch).not.toHaveBeenCalled();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, org)?.key).toBe(intent.key);
    ui.rerender(<SourceRetentionEntry {...context} canAdminister={false} />);
    await waitFor(() => expect(restoreRetentionSourceClassification(sessionStorage, actor, org)).toBeUndefined());
  });
  it('hides an admitted original without deleting it during uncertain parent withdrawal', async () => {
    const intent = RetentionSourceClassificationIntent.review(actor, initial, 'private.original', null, true);
    retainRetentionSourceClassification(sessionStorage, intent);
    transport(); const context = props(), ui = render(<SourceRetentionEntry {...context} />);
    fireEvent.click(screen.getByRole('button', { name: 'Retention classification for this Card' }));
    await screen.findByRole('button', { name: 'Retry original classification' });
    ui.rerender(<SourceRetentionEntry {...context} canAdminister={false} unavailable />);
    expect(screen.queryByRole('button', { name: 'Retry original classification' })).not.toBeInTheDocument();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, org)?.key).toBe(intent.key);
    ui.rerender(<SourceRetentionEntry {...context} canAdminister={false} />);
    await waitFor(() => expect(restoreRetentionSourceClassification(sessionStorage, actor, org)).toBeUndefined());
  });
  it.each(['card', 'cardComment', 'attachment'] as const)('opens %s from actual current context and submits its reviewed identity', async kind => {
    const source = { ...initial, kind, recordId: kind === 'card' ? card : other, parentCardId: kind === 'card' ? null : card,
      sourceVersion: kind === 'card' ? 3 : 2 };
    const fetch = transport(source); render(<SourceRetentionEntry {...props()} kind={kind} recordId={source.recordId} sourceVersion={source.sourceVersion} />);
    expect(fetch).not.toHaveBeenCalled();
    await open(kind === 'card' ? 'Card' : kind === 'cardComment' ? 'Comment' : 'Attachment');
    expect(fetch.mock.calls.some(([path]) => path === `/boards/${board}`)).toBe(true);
    expect(fetch.mock.calls.filter(([path]) => path.includes('retention-policies')).every(([path]) => path.endsWith('after=ffffffff-ffff-ffff-ffff-ffffffffffff'))).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Review source classification' }));
    fireEvent.change(await screen.findByLabelText('Record category *'), { target: { value: 'explicit.context' } });
    fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await screen.findByText('Classification confirmed. The original source and audit evidence are preserved.');
    const requests = fetch.mock.calls.filter(([, options]) => options?.method === 'POST'); expect(requests).toHaveLength(1);
    expect(JSON.parse(requests[0][1]!.body as string)).toEqual({ review: source, category: 'explicit.context', jurisdiction: null, confirmed: true });
  });
  it('offers no new source review or mutation when Organization administration is denied', async () => {
    const fetch = transport(initial, { organizationDenied: true }); render(<SourceRetentionEntry {...props()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Retention classification for this Card' }));
    await screen.findByText('Retention classification access is unavailable.');
    expect(screen.queryByRole('button', { name: 'Review source classification' })).not.toBeInTheDocument();
    expect(fetch.mock.calls.filter(([path]) => path.includes('retention-sources'))).toHaveLength(0);
  });
  it('preserves a draft while parent access is uncertain and rechecks current authority before reopening it', async () => {
    transport(); const context = props(), ui = render(<SourceRetentionEntry {...context} />); await open();
    fireEvent.click(screen.getByRole('button', { name: 'Review source classification' }));
    fireEvent.change(await screen.findByLabelText('Record category *'), { target: { value: 'preserved.draft' } });
    ui.rerender(<SourceRetentionEntry {...context} unavailable />);
    expect(screen.queryByLabelText('Record category *')).not.toBeInTheDocument();
    ui.rerender(<SourceRetentionEntry {...context} />);
    expect(await screen.findByLabelText('Record category *')).toHaveValue('preserved.draft');
  });
  it('retains unrelated source recovery after Board denial but clears it after Organization withdrawal', async () => {
    const unrelated = { ...initial, recordId: other, cardId: other };
    const intent = RetentionSourceClassificationIntent.review(actor, unrelated, 'private.other', null, true);
    retainRetentionSourceClassification(sessionStorage, intent);
    const server: Server = {}; transport(initial, server); render(<SourceRetentionEntry {...props()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Retention classification for this Card' }));
    await screen.findByRole('button', { name: 'Retry original classification' });
    server.boardDenied = true; act(() => live.invalidate!());
    await screen.findByText('Retention classification access is unavailable.');
    expect(restoreRetentionSourceClassification(sessionStorage, actor, org)?.key).toBe(intent.key);
    expect(screen.queryByText('private.other')).not.toBeInTheDocument();
    server.organizationDenied = true; fireEvent.click(screen.getByRole('button', { name: 'Retry retention access' }));
    await waitFor(() => expect(restoreRetentionSourceClassification(sessionStorage, actor, org)).toBeUndefined());
  });
  it('fences private evidence after an identity invalidation with uncertain transport', async () => {
    const server: Server = {}; transport(initial, server); render(<SourceRetentionEntry {...props()} />); await open();
    fireEvent.click(screen.getByRole('button', { name: 'Review source classification' }));
    await screen.findByLabelText('Record category *'); server.temporaryFailure = true;
    act(() => live.invalidate!()); await screen.findByText('Retention access could not be confirmed. Original work is retained; retry the access check.');
    expect(screen.queryByLabelText('Record category *')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm classification' })).not.toBeInTheDocument();
  });
  it('retains the unsent draft when the user hides and reopens the same source disclosure', async () => {
    transport(); render(<SourceRetentionEntry {...props()} />); await open();
    fireEvent.click(screen.getByRole('button', { name: 'Review source classification' }));
    fireEvent.change(await screen.findByLabelText('Record category *'), { target: { value: 'unsent.draft' } });
    fireEvent.click(screen.getByRole('button', { name: 'Hide retention classification' }));
    expect(screen.queryByRole('button', { name: 'Confirm classification' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retention classification for this Card' }));
    expect(await screen.findByLabelText('Record category *')).toHaveValue('unsent.draft');
  });
});
