import { act, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardInvitationPage, OrganizationInvitationPage } from './OrganizationInvitationPage';
import { watchOrganizationMetadata } from './organizationMetadataLive';
import { watchBoard } from '../../api/boardLive';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: vi.fn(() => vi.fn()) }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn(() => vi.fn()) }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222', board = '33333333-3333-4333-8333-333333333333';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const organizationAdmission = { organizationId: org, actorRole: 0, member: { userId: actor, role: 0 } };
const boardAdmission = { board: { id: board, organizationId: org, name: 'Private held-read Board', lifecycleState: 'active' }, access: { canAdminister: true } };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
const count = (rows: Observation[], action: string, kind: string) => rows.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);

beforeEach(() => { vi.clearAllMocks(); configureActivityTelemetry(true); });
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); sessionStorage.clear(); });

for (const boardSurface of [false, true]) for (const outcome of ['healthy', 'rejected'] as const) {
  it(`PRD-03-VS-05/AC-01: ${boardSurface ? 'Board' : 'Organization'} ${outcome} permission read becomes obsolete without a false outcome`, async () => {
    let release!: (response: Response) => void, fail!: (reason: Error) => void;
    const held = new Promise<Response>((resolveHeld, rejectHeld) => { release = resolveHeld; fail = rejectHeld; });
    let admissionReads = 0;
    const admission = boardSurface ? boardAdmission : organizationAdmission;
    const admissionPath = boardSurface ? `/boards/${board}` : `/organizations/${org}/members/${actor}`;
    const fetcher = vi.fn(async (path: string, _init?: RequestInit) => {
      if (path === '/me/activity-client-events') return reply(undefined, 204);
      if (path === '/me') return reply(profile);
      expect(path).toBe(admissionPath);
      return ++admissionReads === 2 ? held : reply(admission);
    });
    vi.stubGlobal('fetch', fetcher);
    render(<RouterProvider router={createMemoryRouter([
      { path: '/app/:organizationId/invite', element: <OrganizationInvitationPage /> },
      { path: '/app/:organizationId/boards/:boardId/invite', element: <BoardInvitationPage /> },
    ], { initialEntries: [boardSurface ? `/app/${org}/boards/${board}/invite` : `/app/${org}/invite`] })} />);
    await screen.findByLabelText(/^Invitation email/);
    const invalidate = async () => {
      if (boardSurface) {
        await waitFor(() => expect(vi.mocked(watchBoard)).toHaveBeenCalled());
        act(() => vi.mocked(watchBoard).mock.calls.at(-1)![0].invalidate());
      } else {
        await waitFor(() => expect(vi.mocked(watchOrganizationMetadata)).toHaveBeenCalled());
        act(() => vi.mocked(watchOrganizationMetadata).mock.calls.at(-1)![0].invalidate());
      }
    };
    // First current source change starts the held read. A second makes its
    // authority generation obsolete while preserving a queued fresh check.
    await invalidate(); await waitFor(() => expect(admissionReads).toBe(2));
    await invalidate();
    await act(async () => {
      if (outcome === 'healthy') release(reply(admission));
      else fail(new Error('Private superseded transport failure'));
      await Promise.resolve();
    });
    await waitFor(() => expect(admissionReads).toBe(3));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create invitation' })).toBeEnabled());
    expect(screen.queryByText('Unable to verify current invitation permissions. Please retry.')).not.toBeInTheDocument();
    await act(async () => { await flushActivityTelemetry(); });
    const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events').map(call => JSON.parse(call[1]?.body as string));
    expect(reports.length).toBeGreaterThan(0);
    const events = reports.flatMap(report => report.events) as Observation[];
    const prefix = boardSurface ? 'board_invitation' : 'organization_invitation';
    expect(count(events, prefix + '_read', 'exception')).toBe(0);
    expect(count(events, prefix + '_read', 'failure')).toBe(0);
    expect(count(events, prefix + '_read', 'success')).toBe(2);
    expect(count(events, prefix + '_disclosure', 'open')).toBe(1);
    expect(events.filter(row => row.action.endsWith('_create'))).toEqual([]);
    expect(fetcher.mock.calls.filter(call => call[0] !== '/me/activity-client-events' && call[1]?.method === 'POST')).toHaveLength(0);
    const text = JSON.stringify(reports);
    for (const value of [org, actor, board, boardAdmission.board.name, 'Private superseded transport failure', 'Idempotency-Key', 'expectedActorId', 'invitationToken']) expect(text).not.toContain(value);
    for (const row of events) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  });
}
