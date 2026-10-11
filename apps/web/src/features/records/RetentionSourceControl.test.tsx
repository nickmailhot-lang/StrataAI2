import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { RetentionSourceControl, type RetentionSourceControlProps } from './RetentionSourceControl';
import { RetentionSourceClassificationIntent, type RetentionSourceReview } from './retentionSourceClient';
import { restoreRetentionSourceClassification, retainRetentionSourceClassification } from './retentionSourceRecovery';

const organization = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const card = '33333333-3333-4333-8333-333333333333', board = '44444444-4444-4444-8444-444444444444';
const list = '55555555-5555-4555-8555-555555555555', key = '66666666-6666-4666-8666-666666666666';
const review: RetentionSourceReview = { organizationId: organization, recordId: card, kind: 'card', parentCardId: null,
  cardId: card, boardId: board, listId: list, cardVersion: 4, listVersion: 2, sourceVersion: 4,
  cardState: 'active', listState: 'archived', sourceState: 'active', createdAt: '2026-10-11T12:00:00Z' };
const receipt = { reference: { organizationId: organization, recordClass: 'operationalWork', recordId: card, kind: 'card',
  parentCardId: null, contextVersion: 1, originalBoardId: board, sourceVersion: 4 }, category: 'maintenance', jurisdiction: null,
  actorId: actor, eventId: key, registeredAt: '2026-10-11T12:00:01Z' };
const props = (): RetentionSourceControlProps => ({ organizationId: organization, actorId: actor, kind: 'card', recordId: card,
  cardId: card, boardId: board, cardVersion: 4, sourceVersion: 4, authority: 'admitted', accessLost: vi.fn(), completed: vi.fn() });
function transport(post = (_options: RequestInit) => Promise.resolve(new Response(JSON.stringify(receipt)))) {
  const fetch = vi.fn(async (url: string, options: RequestInit = {}) => options.method === 'POST' ? post(options)
    : new Response(JSON.stringify(url === '/me'
      ? { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' } : review)));
  vi.stubGlobal('fetch', fetch); return fetch;
}
async function draft() {
  fireEvent.click(screen.getByRole('button', { name: 'Review source classification' }));
  fireEvent.change(await screen.findByLabelText('Record category *'), { target: { value: 'maintenance' } });
  fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification' }));
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); });
describe('PRD-70 source-context review and acknowledgment recovery control', () => {
  it('requires visible evidence and human confirmation before one authoritative classification', async () => {
    const fetch = transport(), callbacks = props(); render(<RetentionSourceControl {...callbacks} />);
    fireEvent.click(screen.getByRole('button', { name: 'Review source classification' }));
    await screen.findByLabelText('Record category *');
    expect(screen.getByText('Source revision 4; Card revision 4; List revision 2.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Confirm classification' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Record category *'), { target: { value: 'maintenance' } });
    fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await screen.findByText('Classification confirmed. The original source and audit evidence are preserved.');
    expect(callbacks.completed).toHaveBeenCalledWith(receipt);
    expect(screen.getByRole('button', { name: 'Review source classification' })).toBeDisabled();
    expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1);
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)).toBeUndefined();
  });
  it('restores a lost acknowledgment after remount and movement using only the original body and key', async () => {
    const bodies: string[] = [], keys: string[] = []; let calls = 0;
    const fetch = transport(async options => {
      bodies.push(options.body as string); keys.push(new Headers(options.headers).get('Idempotency-Key')!);
      if (++calls === 1) throw new TypeError('Lost response'); return new Response(JSON.stringify(receipt));
    });
    const first = render(<RetentionSourceControl {...props()} />); await draft();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await screen.findByText('The acknowledgment could not be confirmed. The original submission is retained for retry.');
    first.unmount();
    render(<RetentionSourceControl {...props()} boardId={list} cardVersion={5} sourceVersion={5} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original classification' }));
    await screen.findByText('Classification confirmed. The original source and audit evidence are preserved.');
    expect(bodies).toEqual([bodies[0], bodies[0]]); expect(keys).toEqual([keys[0], keys[0]]);
    expect(fetch.mock.calls.filter(([url]) => url.endsWith('/review'))).toHaveLength(1);
  });
  it('blocks a stale source revision while retaining the unsent category draft', async () => {
    const fetch = transport(), context = props(), ui = render(<RetentionSourceControl {...context} />); await draft();
    ui.rerender(<RetentionSourceControl {...context} cardVersion={5} sourceVersion={5} />);
    expect(screen.getByRole('button', { name: 'Confirm classification' })).toBeDisabled();
    expect(screen.getByLabelText('Record category *')).toHaveValue('maintenance');
    expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(0);
  });
  it('hides private evidence during uncertainty and clears originals on definitive denial', async () => {
    transport(); const context = props(), ui = render(<RetentionSourceControl {...context} />); await draft();
    retainRetentionSourceClassification(sessionStorage, RetentionSourceClassificationIntent.review(actor, review, 'maintenance', null, true, key));
    ui.rerender(<RetentionSourceControl {...context} authority="uncertain" />);
    expect(screen.queryByLabelText('Record category *')).not.toBeInTheDocument();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)?.key).toBe(key);
    ui.rerender(<RetentionSourceControl {...context} authority="denied" />);
    await waitFor(() => expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)).toBeUndefined());
    expect(screen.queryByText(/Source revision/)).not.toBeInTheDocument();
  });
  it('hides an original from another source and refuses fresh classification while recovery is pending', async () => {
    retainRetentionSourceClassification(sessionStorage, RetentionSourceClassificationIntent.review(actor, review, 'private.category', 'Private jurisdiction', true, key));
    render(<RetentionSourceControl {...props()} recordId={list} cardId={list} />);
    await screen.findByRole('button', { name: 'Retry original classification' });
    expect(screen.queryByText('private.category')).not.toBeInTheDocument();
    expect(screen.queryByText('Private jurisdiction')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Review source classification' })).not.toBeInTheDocument();
  });
  it('preserves another source original when the current source authority is denied', async () => {
    const intent = RetentionSourceClassificationIntent.review(actor, review, 'private.category', 'Private jurisdiction', true, key);
    retainRetentionSourceClassification(sessionStorage, intent);
    const ui = render(<RetentionSourceControl {...props()} recordId={list} cardId={list} />);
    await screen.findByRole('button', { name: 'Retry original classification' });
    ui.rerender(<RetentionSourceControl {...props()} recordId={list} cardId={list} authority="denied" />);
    await screen.findByText('Source classification access is unavailable.');
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)?.key).toBe(key);
    expect(screen.queryByText('private.category')).not.toBeInTheDocument();
  });
  it('blocks new delivery when recovery storage is corrupt', async () => {
    sessionStorage.setItem(`strataai:retention-source-classification:v1:${actor}:${organization}`, 'corrupt');
    const fetch = transport(); render(<RetentionSourceControl {...props()} />);
    await screen.findByText('Original classification recovery is unavailable. New classifications are blocked.');
    expect(screen.getByRole('button', { name: 'Review source classification' })).toBeDisabled();
    expect(fetch).not.toHaveBeenCalled();
  });
  it('does not acknowledge or replace a definitively refused original', async () => {
    transport(async () => new Response(JSON.stringify({ code: 'idempotency_key_expired' }), { status: 409 }));
    const context = props(); render(<RetentionSourceControl {...context} />); await draft();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await screen.findByText('The original submission was refused. Do not replace its retry key; review the source or contact support.');
    expect(screen.getByRole('button', { name: 'Retry original classification' })).toBeDisabled();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)).toBeDefined();
    expect(context.completed).not.toHaveBeenCalled();
  });
  it('sends nothing on storage failure and retains the same unsent key for explicit retry', async () => {
    const fetch = transport(); render(<RetentionSourceControl {...props()} />); await draft();
    const write = vi.spyOn(Storage.prototype, 'setItem').mockImplementationOnce(() => { throw new Error('Storage unavailable'); });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await screen.findByText('Browser recovery could not be verified. No classification was sent. Retry the original after recovery is available.');
    const unsent = JSON.parse(write.mock.calls[0][1]) as { key: string };
    expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Retry original classification' }));
    await screen.findByText('Classification confirmed. The original source and audit evidence are preserved.');
    const posted = fetch.mock.calls.find(([, options]) => options?.method === 'POST')![1];
    expect(new Headers(posted?.headers).get('Idempotency-Key')).toBe(unsent.key);
  });
  it('clears evidence and pending recovery on a definitive server denial', async () => {
    transport(async () => new Response('{}', { status: 404 })); const context = props();
    render(<RetentionSourceControl {...context} />); await draft();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await waitFor(() => expect(context.accessLost).toHaveBeenCalled());
    expect(screen.queryByLabelText('Record category *')).not.toBeInTheDocument();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)).toBeUndefined();
    expect(context.completed).not.toHaveBeenCalled();
  });
  it('fences a late successful response after access becomes uncertain', async () => {
    let finish: ((response: Response) => void) | undefined;
    const fetch = transport(() => new Promise<Response>(resolve => { finish = resolve; })), context = props();
    const ui = render(<RetentionSourceControl {...context} />); await draft();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm classification' }));
    await waitFor(() => expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1));
    ui.rerender(<RetentionSourceControl {...context} authority="uncertain" />);
    finish!(new Response(JSON.stringify(receipt)));
    await waitFor(() => expect(screen.getByText('Checking source classification access. Original work is retained.')).toBeVisible());
    expect(context.completed).not.toHaveBeenCalled();
    expect(restoreRetentionSourceClassification(sessionStorage, actor, organization)).toBeDefined();
  });
  it('focuses the category after review and exposes named native confirmation controls', async () => {
    transport(); render(<RetentionSourceControl {...props()} />);
    const start = screen.getByRole('button', { name: 'Review source classification' }); start.focus(); fireEvent.click(start);
    const input = await screen.findByLabelText('Record category *');
    await waitFor(() => expect(input).toHaveFocus()); fireEvent.change(input, { target: { value: 'maintenance' } });
    screen.getByLabelText('Jurisdiction (optional)').focus();
    const confirmation = screen.getByRole('checkbox'); confirmation.focus(); fireEvent.click(confirmation);
    const save = screen.getByRole('button', { name: 'Confirm classification' }); save.focus(); expect(save).toHaveFocus(); fireEvent.click(save);
    await screen.findByText('Classification confirmed. The original source and audit evidence are preserved.');
  });
});
