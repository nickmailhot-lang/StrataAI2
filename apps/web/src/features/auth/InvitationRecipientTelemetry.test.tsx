import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationsPage } from './InvitationsPage';
import { InvitationLinkPage } from './InvitationLinkPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

vi.mock('./invitationRecipientLive', () => ({ watchInvitationRecipient: vi.fn((options: { invalidate(reason: string): void }) => { options.invalidate('reset'); return vi.fn(); }) }));
const actor = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', other = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const token = 'private_recipient_proof_1234567890123456789012345';
const item = { id: '11111111-1111-4111-8111-111111111111', organizationId: '22222222-2222-4222-8222-222222222222',
  organizationName: 'Private recipient council', surface: 'INTERNAL', targetRole: 'MEMBER', expiresAt: '2035-01-08T18:00:00Z' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });

for (const link of [false, true]) {
  const prefix = link ? 'invitation_link' : 'invitation_recipient';
  function setup(mode = 'normal') {
    let writes = 0, reads = 0, switched = false;
    let release: (() => void) | undefined;
    const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me/activity-client-events') { if (mode === 'reporting') throw new Error('Private reporting fault'); return new Response(null, { status: 204 }); }
      if (path === '/me') return reply({ id: switched ? other : actor });
      if (path.startsWith('/me/invitations/') && options?.method === 'POST') {
        writes++;
        if (mode === 'lost' && writes === 1) throw new Error('Private committed response lost');
        if (mode === 'account') switched = true;
        if (mode === 'late') await new Promise<void>(resolve => { release = resolve; });
        if (mode === 'conflict') return reply({ code: 'private_conflict' }, 409);
        return reply({ invitationId: item.id, organizationId: item.organizationId, surface: item.surface, targetRole: item.targetRole });
      }
      if (path.startsWith('/invitations/review?') || path.startsWith('/me/invitations?')) {
        reads++;
        const body = mode === 'malformed' ? { ...item, expiresAt: '2035-02-30T12:00:00Z' } : item;
        if (mode === 'read-failure' && reads === 1) throw new Error('Private discovery outage');
        return reply(link ? body : { items: [body], nextCursor: null });
      }
      throw new Error('Unexpected recipient operation');
    });
    vi.stubGlobal('fetch', fetcher);
    const view = render(<RouterProvider router={createMemoryRouter([
      { path: '/app/invitations', element: <InvitationsPage /> }, { path: '/invitation', element: <InvitationLinkPage /> },
      { path: '/login', element: <h1>Sign in destination</h1> },
    ], { initialEntries: [link ? `/invitation#token=${token}` : '/app/invitations'] })} />);
    return { ...view, fetcher, writes: () => writes, release: () => release?.() };
  }
  async function review() {
    if (link) fireEvent.click(await screen.findByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: item.organizationName });
  }
  async function reports(fetcher: ReturnType<typeof vi.fn>) {
    const rows: Observation[] = [];
    for (let index = 0; index < 3; index++) await flushActivityTelemetry();
    for (const call of fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')) rows.push(...JSON.parse(call[1].body as string).events);
    for (const value of [actor, other, token, item.id, item.organizationId, item.organizationName, item.expiresAt, 'Private reporting fault', 'Private committed response lost', 'private_conflict', 'expectedActorId']) expect(JSON.stringify(rows)).not.toContain(value);
    for (const row of rows) {
      expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60000); }
    }
    return rows;
  }
  function count(rows: Observation[], action: string, kind: string) { return rows.filter(row => row.action === prefix + '_' + action && row.kind === kind).reduce((sum, row) => sum + row.count, 0); }
  it(`${prefix}: reports only admitted disclosure and protected read/review`, async () => {
    const state = setup(); await review();
    if (!link) { fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' })); await screen.findByRole('heading', { name: item.organizationName }); }
    const rows = await reports(state.fetcher); expect(count(rows, 'disclosure', 'open')).toBe(1);
    expect(count(rows, link ? 'review' : 'read', 'success')).toBeGreaterThanOrEqual(1);
    if (!link) expect(count(rows, 'read', 'retry')).toBe(1);
    expect(count(rows, 'accept', 'use')).toBe(0); expect(state.writes()).toBe(0);
  });
  it(`${prefix}: malformed metadata cannot count as disclosure or read success`, async () => {
    const state = setup('malformed'); if (link) fireEvent.click(await screen.findByRole('button', { name: 'Review invitation' }));
    await screen.findByText(link ? 'The invitation could not be reviewed. Wait and retry.' : 'Unable to load invitations. Please refresh and try again.');
    const rows = await reports(state.fetcher); expect(count(rows, 'disclosure', 'open')).toBe(0);
    expect(count(rows, link ? 'review' : 'read', 'failure')).toBe(1); expect(count(rows, link ? 'review' : 'read', 'success')).toBe(0);
  });
  it(`${prefix}: explicit read recovery reports retry without accepting`, async () => {
    const state = setup('read-failure'); if (link) fireEvent.click(await screen.findByRole('button', { name: 'Review invitation' }));
    await screen.findByText(link ? 'The invitation could not be reviewed. Wait and retry.' : 'Unable to load invitations. Please refresh and try again.');
    fireEvent.click(screen.getByRole('button', { name: link ? 'Review invitation' : 'Refresh invitations' }));
    await screen.findByRole('heading', { name: item.organizationName });
    const rows = await reports(state.fetcher); const action = link ? 'review' : 'read';
    expect(count(rows, action, 'retry')).toBe(1); expect(count(rows, action, 'failure')).toBe(1); expect(count(rows, action, 'success')).toBe(1); expect(state.writes()).toBe(0);
  });
  it(`${prefix}: acceptance requires acknowledgment and records complete operation latency`, async () => {
    const state = setup(); await review(); fireEvent.click(screen.getByRole('button', { name: link ? 'Accept reviewed invitation' : /^Accept invitation to/ }));
    await screen.findByText(link ? 'Invitation acceptance acknowledged.' : /Invitation to .* accepted\./);
    const rows = await reports(state.fetcher); expect(count(rows, 'accept', 'use')).toBe(1); expect(count(rows, 'accept', 'success')).toBe(1); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: lost replies retain explicit original acceptance recovery`, async () => {
    const state = setup('lost'); await review(); fireEvent.click(screen.getByRole('button', { name: link ? 'Accept reviewed invitation' : /^Accept invitation to/ }));
    const retry = await screen.findByRole('button', { name: 'Retry invitation acceptance' }); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
    await screen.findByText(link ? 'Invitation acceptance acknowledged.' : /Invitation to .* accepted\./);
    const rows = await reports(state.fetcher); expect(count(rows, 'accept', 'use')).toBe(1); expect(count(rows, 'accept', 'retry')).toBe(1);
    expect(count(rows, 'accept', 'failure')).toBe(1); expect(count(rows, 'accept', 'success')).toBe(1);
    const commands = state.fetcher.mock.calls.filter(call => call[0].startsWith('/me/invitations/') && call[1]?.method === 'POST');
    expect(commands).toHaveLength(2); expect(commands[1][0]).toBe(commands[0][0]); expect(commands[1][1]!.body).toBe(commands[0][1]!.body);
  });
  it(`${prefix}: final account replacement cannot report acceptance success`, async () => {
    const state = setup('account'); await review(); fireEvent.click(screen.getByRole('button', { name: link ? 'Accept reviewed invitation' : /^Accept invitation to/ }));
    await screen.findByText(link ? 'Sign in with the invited account, then review the invitation again.' : 'Sign in destination');
    const rows = await reports(state.fetcher); expect(count(rows, 'accept', 'success')).toBe(0); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: conflicts cannot become success or cause another command`, async () => {
    const state = setup('conflict'); await review(); fireEvent.click(screen.getByRole('button', { name: link ? 'Accept reviewed invitation' : /^Accept invitation to/ }));
    await screen.findByText(link ? 'This link is unavailable to your account. Verify your email or check your current invitations.' : 'This invitation is no longer available to your account. Refresh to check current invitations.');
    const rows = await reports(state.fetcher); expect(count(rows, 'accept', 'conflict')).toBe(1); expect(count(rows, 'accept', 'failure')).toBe(1); expect(count(rows, 'accept', 'success')).toBe(0); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: unmount fences late acknowledgment`, async () => {
    const state = setup('late'); await review(); fireEvent.click(screen.getByRole('button', { name: link ? 'Accept reviewed invitation' : /^Accept invitation to/ }));
    await waitFor(() => expect(state.writes()).toBe(1)); await reports(state.fetcher); state.unmount();
    await act(async () => state.release()); const rows = await reports(state.fetcher);
    expect(count(rows, 'accept', 'success')).toBe(0); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: optional reporting failure cannot repeat acknowledged acceptance`, async () => {
    const state = setup('reporting'); await review(); fireEvent.click(screen.getByRole('button', { name: link ? 'Accept reviewed invitation' : /^Accept invitation to/ }));
    await screen.findByText(link ? 'Invitation acceptance acknowledged.' : /Invitation to .* accepted\./);
    const rows = await reports(state.fetcher); expect(count(rows, 'accept', 'success')).toBe(1); expect(state.writes()).toBe(1);
  });
}
