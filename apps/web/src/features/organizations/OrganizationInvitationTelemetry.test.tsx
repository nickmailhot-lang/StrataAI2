import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationInvitationPage, BoardInvitationPage } from './OrganizationInvitationPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: vi.fn(() => vi.fn()) }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => vi.fn()) }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-4111-8111-111111111111'; const actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333'; const invitation = '44444444-4444-4444-8444-444444444444';
const otherActor = '55555555-5555-4555-8555-555555555555'; const email = 'private-recipient@example.test';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const organizationAdmission = { organizationId: org, actorRole: 0, member: { userId: actor, role: 0 } };
const boardAdmission = { board: { id: board, organizationId: org, name: 'Private invitation Board', lifecycleState: 'active' }, access: { canAdminister: true } };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
const commands = (fetcher: ReturnType<typeof vi.fn>) => fetcher.mock.calls.filter(call => call[0] !== '/me/activity-client-events' && call[1]?.method === 'POST');
function mount(boardSurface: boolean) {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/invite', element: <OrganizationInvitationPage /> },
    { path: '/app/:organizationId/boards/:boardId/invite', element: <BoardInvitationPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
  ], { initialEntries: [boardSurface ? `/app/${org}/boards/${board}/invite` : `/app/${org}/invite`] })} />);
}
async function submit() {
  fireEvent.change(await screen.findByLabelText(/^Invitation email/), { target: { value: email } });
  fireEvent.click(screen.getByRole('button', { name: 'Create invitation' }));
}
async function observations(fetcher: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1]?.body as string); const text = JSON.stringify(body);
  const keys = commands(fetcher).map(call => new Headers(call[1]?.headers).get('Idempotency-Key')).filter((value): value is string => !!value);
  for (const value of [org, actor, otherActor, board, invitation, email, boardAdmission.board.name, 'Private transport failure', 'Idempotency-Key', 'expectedActorId', 'invitationToken', 'targetRole', ...keys]) expect(text).not.toContain(value);
  for (const row of body.events as Observation[]) {
    expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
    if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
  }
  return body.events as Observation[];
}
const count = (rows: Observation[], action: string, kind: string) => rows.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
beforeEach(() => configureActivityTelemetry(true));
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); sessionStorage.clear(); });

for (const boardSurface of [false, true]) {
  const prefix = boardSurface ? 'board_invitation' : 'organization_invitation';
  const admission = boardSurface ? boardAdmission : organizationAdmission;
  const ack = { id: invitation, organizationId: org, email, surface: 'INTERNAL', targetRole: 'MEMBER', invitationToken: null,
    expiresAt: '2026-12-01T00:00:00Z', ...(boardSurface ? { boardTarget: { boardId: board, role: 'MEMBER' } } : {}) };
  it(`${prefix}: observes one admitted disclosure and permission read without an invitation command`, async () => {
    const fetcher = vi.fn(async (path: string, _init?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204) : reply(path === '/me' ? profile : admission));
    vi.stubGlobal('fetch', fetcher); mount(boardSurface); await screen.findByLabelText(/^Invitation email/);
    const events = await observations(fetcher); expect(count(events, prefix + '_disclosure', 'open')).toBe(1); expect(count(events, prefix + '_read', 'success')).toBe(1);
    expect(events.filter(row => row.action.endsWith('_create'))).toEqual([]); expect(commands(fetcher)).toHaveLength(0);
  });
  it(`${prefix}: observes explicit permission retry after a failed read`, async () => {
    let reads = 0;
    const fetcher = vi.fn(async (path: string, _init?: RequestInit) => {
      if (path === '/me/activity-client-events') return reply(undefined, 204);
      if (path === '/me') return reply(profile);
      return ++reads === 1 ? reply({}, 503) : reply(admission);
    }); vi.stubGlobal('fetch', fetcher); mount(boardSurface);
    await screen.findByText('Unable to verify current invitation permissions. Please retry.');
    fireEvent.click(screen.getByRole('button', { name: 'Retry permission check' })); await screen.findByLabelText(/^Invitation email/);
    const events = await observations(fetcher);
    for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(count(events, prefix + '_read', kind)).toBe(1);
    expect(count(events, prefix + '_disclosure', 'open')).toBe(1); expect(commands(fetcher)).toHaveLength(0);
  });
  it(`${prefix}: observes lost acknowledgment and exact original creation recovery`, async () => {
    let writes = 0;
    const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
      if (path === '/me/activity-client-events') return reply(undefined, 204);
      if (init?.method === 'POST') { if (++writes === 1) throw new Error('Private transport failure'); return reply(ack, 201); }
      return reply(path === '/me' ? profile : admission);
    }); vi.stubGlobal('fetch', fetcher); mount(boardSurface); await submit();
    const retry = await screen.findByRole('button', { name: 'Retry same invitation' }); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
    await screen.findByRole('button', { name: 'Create another invitation' }); const events = await observations(fetcher);
    for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(count(events, prefix + '_create', kind)).toBe(1);
    const sent = commands(fetcher); expect(sent).toHaveLength(2); expect(sent[1][0]).toBe(sent[0][0]); expect(sent[1][1]?.body).toBe(sent[0][1]?.body);
    expect(new Headers(sent[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(sent[0][1]?.headers).get('Idempotency-Key'));
  });
  it(`${prefix}: observes refused key conflict without success or an automatic request`, async () => {
    const fetcher = vi.fn(async (path: string, init?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204)
      : init?.method === 'POST' ? reply({ code: 'idempotency_key_reused' }, 409) : reply(path === '/me' ? profile : admission));
    vi.stubGlobal('fetch', fetcher); mount(boardSurface); await submit();
    await screen.findByText('This request cannot be retried. Review existing invitations before creating another.');
    const events = await observations(fetcher); expect(count(events, prefix + '_create', 'conflict')).toBe(1); expect(count(events, prefix + '_create', 'failure')).toBe(1);
    expect(count(events, prefix + '_create', 'success')).toBe(0); expect(commands(fetcher)).toHaveLength(1);
  });
  it(`${prefix}: withholds disclosure when permission admission refuses access`, async () => {
    const fetcher = vi.fn(async (path: string, _init?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204) : path === '/me' ? reply(profile) : reply({}, 403));
    vi.stubGlobal('fetch', fetcher); mount(boardSurface); await screen.findByText(/invitations are unavailable to your account/);
    const events = await observations(fetcher); expect(count(events, prefix + '_disclosure', 'open')).toBe(0); expect(count(events, prefix + '_read', 'failure')).toBe(1);
  });
  it(`${prefix}: cannot report creation success when the final account check switches principal`, async () => {
    let submitted = false;
    const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
      if (path === '/me/activity-client-events') return reply(undefined, 204);
      if (path === '/me') return reply(submitted ? { ...profile, id: otherActor } : profile);
      if (init?.method === 'POST') { submitted = true; return reply(ack, 201); }
      return reply(admission);
    }); vi.stubGlobal('fetch', fetcher); mount(boardSurface); await submit(); await screen.findByText('Sign in destination');
    const events = await observations(fetcher); expect(count(events, prefix + '_create', 'success')).toBe(0); expect(commands(fetcher)).toHaveLength(1);
  });
  it(`${prefix}: reporting failure preserves acknowledgment and cannot resend the invitation`, async () => {
    let writes = 0;
    const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
      if (path === '/me/activity-client-events') throw new Error('Private transport failure');
      if (init?.method === 'POST') { writes++; return reply(ack, 201); }
      return reply(path === '/me' ? profile : admission);
    }); vi.stubGlobal('fetch', fetcher); mount(boardSurface); await submit(); await screen.findByRole('button', { name: 'Create another invitation' });
    await flushActivityTelemetry(); expect(writes).toBe(1); expect(fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Create another invitation' })).toBeVisible();
  });
}
