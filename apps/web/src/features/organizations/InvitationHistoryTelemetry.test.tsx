import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationInvitationHistoryPage, BoardInvitationHistoryPage } from './OrganizationInvitationHistoryPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: vi.fn(() => vi.fn()) }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn((options: { status(value: string): void }) => { options.status('live'); return vi.fn(); }) }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', invitation = '44444444-4444-4444-8444-444444444444';
const other = '55555555-5555-4555-8555-555555555555';
const profile = { id: actor, locale: 'en-CA', timezone: 'UTC' };
const row = { id: invitation, email: 'private-history-recipient@example.test', surface: 'INTERNAL', targetRole: 'MEMBER',
  createdAt: '2034-01-01T00:00:00Z', expiresAt: '2035-01-08T18:00:00Z', acceptedAt: null, revokedAt: null, deliveryState: 'SENT' };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
const count = (rows: Observation[], action: string, kind: string) => rows.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
for (const boardSurface of [false, true]) {
  const prefix = boardSurface ? 'board_invitation_history' : 'organization_invitation_history';
  function setup(mode = 'success') {
    let reads = 0, writes = 0, revoked = false;
    const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
      if (path === '/me/activity-client-events') { if (mode === 'reporting') throw new Error('Private reporting fault'); return reply(undefined, 204); }
      if (path === '/me') return reply(mode === 'account' && writes ? { ...profile, id: other } : profile);
      if (init?.method === 'DELETE') {
        writes++; if (mode === 'conflict') return reply({ code: 'private_conflict' }, 409);
        revoked = true; if (mode === 'lost') throw new Error('Private transport fault'); return reply(undefined, 204);
      }
      if (path === `/boards/${board}`) return reply({ board: { id: board, organizationId: org, lifecycleState: 'active', name: 'Private history Board' }, access: { canAdminister: true } });
      reads++; if (mode === 'denied') return reply({}, 403);
      if (mode === 'read-retry' && reads === 1) return reply({}, 503);
      return reply({ items: [{ ...row, revokedAt: revoked ? '2034-01-02T00:00:00Z' : null,
        ...(boardSurface ? { boardTarget: { boardId: board, role: 'MEMBER' } } : {}) }], nextCursor: null });
    }); vi.stubGlobal('fetch', fetcher);
    render(<RouterProvider router={createMemoryRouter([
      { path: '/app/:organizationId/invitations', element: <OrganizationInvitationHistoryPage /> },
      { path: '/app/:organizationId/boards/:boardId/invitations', element: <BoardInvitationHistoryPage /> },
    ], { initialEntries: [boardSurface ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`] })} />);
    return { fetcher, writes: () => writes };
  }
  async function events(fetcher: ReturnType<typeof vi.fn>) {
    await flushActivityTelemetry(); const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
    const body = JSON.parse(reports[0][1]?.body as string); const text = JSON.stringify(body);
    for (const value of [org, actor, board, invitation, other, row.email, 'Private history Board', 'Private reporting fault', 'Private transport fault', 'private_conflict', 'expectedActorId', 'revokedAt', 'expiresAt']) expect(text).not.toContain(value);
    for (const event of body.events as Observation[]) {
      expect(Object.keys(event).sort()).toEqual(event.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      if (event.durationMs !== undefined) { expect(Number.isFinite(event.durationMs)).toBe(true); expect(event.durationMs).toBeGreaterThanOrEqual(0); expect(event.durationMs).toBeLessThanOrEqual(60_000); }
    } return body.events as Observation[];
  }
  async function review() { fireEvent.click(await screen.findByRole('button', { name: `Revoke invitation for ${row.email}` })); await screen.findByRole('dialog'); }
  async function confirm() { await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' })); }
  it(`${prefix}: counts admitted disclosure and review without treating Cancel as a revocation`, async () => {
    const state = setup(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); const rows = await events(state.fetcher);
    expect(count(rows, prefix + '_disclosure', 'open')).toBe(1); expect(count(rows, prefix + '_read', 'success')).toBe(1);
    expect(count(rows, prefix + '_revoke', 'open')).toBe(1); expect(count(rows, prefix + '_revoke', 'use')).toBe(0); expect(state.writes()).toBe(0);
  });
  it(`${prefix}: counts explicit history retry and complete read outcomes`, async () => {
    const state = setup('read-retry'); await screen.findByText('Unable to confirm invitation history. Please retry.');
    const refresh = screen.getByRole('button', { name: 'Refresh invitations' }); await waitFor(() => expect(refresh).toBeEnabled()); fireEvent.click(refresh);
    await screen.findByRole('heading', { name: row.email }); const rows = await events(state.fetcher);
    for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(count(rows, prefix + '_read', kind)).toBe(1); expect(state.writes()).toBe(0);
  });
  it(`${prefix}: records one acknowledged revocation after final original-account admission`, async () => {
    const state = setup(); await confirm(); await screen.findByText('Invitation revocation confirmed.'); await screen.findByText('Revoked');
    const rows = await events(state.fetcher); expect(count(rows, prefix + '_revoke', 'use')).toBe(1); expect(count(rows, prefix + '_revoke', 'success')).toBe(1);
    expect(count(rows, prefix + '_revoke', 'failure')).toBe(0); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: records uncertainty and read-only original-row recovery without another DELETE`, async () => {
    const state = setup('lost'); await confirm(); await screen.findByText(/Revocation could not be confirmed/);
    const check = await screen.findByRole('button', { name: 'Check revocation' }); await waitFor(() => expect(check).toBeEnabled()); fireEvent.click(check);
    await screen.findByText('Invitation revocation confirmed.'); const rows = await events(state.fetcher);
    for (const kind of ['use', 'exception', 'failure']) expect(count(rows, prefix + '_revoke', kind)).toBe(1);
    expect(count(rows, prefix + '_revoke', 'success')).toBe(0); expect(count(rows, prefix + '_read', 'retry')).toBe(1); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: records an ambiguous conflict without success or an automatic command`, async () => {
    const state = setup('conflict'); await confirm(); await screen.findByText(/Revocation could not be confirmed/); const rows = await events(state.fetcher);
    expect(count(rows, prefix + '_revoke', 'conflict')).toBe(1); expect(count(rows, prefix + '_revoke', 'failure')).toBe(1);
    expect(count(rows, prefix + '_revoke', 'success')).toBe(0); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: final account replacement cannot promote revocation success`, async () => {
    const state = setup('account'); await confirm(); await screen.findByText(/Sign in again to review/); const rows = await events(state.fetcher);
    expect(count(rows, prefix + '_revoke', 'success')).toBe(0); expect(count(rows, prefix + '_revoke', 'failure')).toBe(1);
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(state.writes()).toBe(1);
  });
  it(`${prefix}: denies history without an admitted disclosure`, async () => {
    const state = setup('denied'); await screen.findByText(/invitation administration is unavailable/); const rows = await events(state.fetcher);
    expect(count(rows, prefix + '_disclosure', 'open')).toBe(0); expect(count(rows, prefix + '_read', 'failure')).toBe(1); expect(state.writes()).toBe(0);
  });
  it(`${prefix}: reporting failure cannot repeat an acknowledged command`, async () => {
    const state = setup('reporting'); await confirm(); await screen.findByText('Invitation revocation confirmed.'); await screen.findByText('Revoked');
    await flushActivityTelemetry(); expect(state.fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')).toHaveLength(1); expect(state.writes()).toBe(1);
  });
}
