import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationMembersPage } from './OrganizationMembersPage';
import { configureActivityTelemetry, flushActivityTelemetry } from '../kanban/activityTelemetry';

const live = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const target = '33333333-3333-4333-8333-333333333333';
const otherActor = '55555555-5555-4555-8555-555555555555';
const member = { membershipId: '44444444-4444-4444-8444-444444444444', userId: target,
  displayName: 'Private member name', email: 'private-member@example.test', role: 2, accountStatus: 'ACTIVE',
  emailVerified: true, isUsableOwner: false, joinedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', version: 7 };
const root = `/organizations/${org}/members`;
const directory = { organizationId: org, items: [member], nextCursor: null, actorRole: 0 };
const review = { organizationId: org, member, actorRole: 0 };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
type Observation = { action: string; kind: string; count: number; durationMs?: number };
function mount() {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/members', element: <OrganizationMembersPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
    { path: '/app', element: <h1>Organizations destination</h1> },
  ], { initialEntries: [`/app/${org}/members`] })} />);
}
beforeEach(() => { configureActivityTelemetry(true); live.watch.mockClear(); });
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); });
async function observations(fetcher: ReturnType<typeof vi.fn>) {
  await flushActivityTelemetry();
  const reports = fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events'); expect(reports).toHaveLength(1);
  const body = JSON.parse(reports[0][1]?.body as string); const serialized = JSON.stringify(body);
  const commands = fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE');
  const keys = commands.map(call => new Headers(call[1]?.headers).get('Idempotency-Key')).filter((key): key is string => !!key);
  for (const value of [org, actor, otherActor, target, member.membershipId, member.displayName, member.email,
    'Private transport failure', 'Idempotency-Key', 'expectedVersion', ...keys]) expect(serialized).not.toContain(value);
  for (const row of body.events as Observation[]) {
    expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
    if (row.durationMs !== undefined) expect(row.durationMs).toBeGreaterThanOrEqual(0);
  }
  return body.events as Observation[];
}
const count = (rows: Observation[], action: string, kind: string) => rows.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
async function openReview() {
  fireEvent.click(await screen.findByRole('button', { name: `Review removal of ${member.displayName}` }));
  await screen.findByRole('dialog');
}

it('records one admitted disclosure, explicit directory retry and live recovery without membership data', async () => {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204)
    : reply(path === '/me' ? { id: actor } : directory)); vi.stubGlobal('fetch', fetcher);
  mount(); await screen.findByRole('button', { name: `Review removal of ${member.displayName}` });
  fireEvent.click(screen.getByRole('button', { name: 'Load current members' }));
  await screen.findByRole('button', { name: `Review removal of ${member.displayName}` });
  act(() => live.watch.mock.calls[0][0].reset());
  await screen.findByText('Current members checked. Review a membership again before confirming removal.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_members_disclosure', 'open')).toBe(1);
  expect(count(events, 'organization_members_read', 'retry')).toBe(2);
  expect(count(events, 'organization_members_read', 'success')).toBe(3);
});
it('observes review, lost removal acknowledgment and exact original recovery without another intent', async () => {
  let writes = 0;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return reply({ id: actor });
    if (options?.method === 'DELETE') { if (++writes === 1) throw new Error('Private transport failure'); return reply(undefined, 204); }
    return reply(path === root ? directory : review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await openReview();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member removal' }));
  await screen.findByText(/The removal could not be confirmed/);
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original removal' }));
  await screen.findByText('Original removal acknowledged. Review current membership to check later access.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_member_review', 'success')).toBe(1);
  for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(count(events, 'organization_member_remove', kind)).toBe(1);
  const commands = fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(commands).toHaveLength(2);
  expect(commands[1][0]).toBe(commands[0][0]); expect(commands[1][0]).toContain('expectedVersion=7');
  expect(new Headers(commands[1][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[0][1]?.headers).get('Idempotency-Key'));
});
it('records a refused removal conflict without success or an automatic second command', async () => {
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204)
    : path === '/me' ? reply({ id: actor }) : options?.method === 'DELETE' ? reply({ code: 'member_version_conflict' }, 409)
      : reply(path === root ? directory : review)); vi.stubGlobal('fetch', fetcher); mount(); await openReview();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member removal' }));
  await screen.findByText('The membership changed elsewhere. Review it before confirming removal again.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_member_remove', 'conflict')).toBe(1);
  expect(count(events, 'organization_member_remove', 'failure')).toBe(1);
  expect(count(events, 'organization_member_remove', 'success')).toBe(0);
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
});
it('does not count a disclosure before administrative directory admission', async () => {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204)
    : path === '/me' ? reply({ id: actor }) : reply({}, 403)); vi.stubGlobal('fetch', fetcher); mount();
  await screen.findByText('Organization members are unavailable to your account.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_members_disclosure', 'open')).toBe(0);
  expect(count(events, 'organization_members_read', 'failure')).toBe(1);
});
it('does not claim removal success before the post-response account fence', async () => {
  let swapped = false;
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') return reply(undefined, 204);
    if (path === '/me') return reply({ id: swapped ? otherActor : actor });
    if (options?.method === 'DELETE') { swapped = true; return reply(undefined, 204); }
    return reply(path === root ? directory : review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await openReview();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member removal' })); await screen.findByText('Sign in destination');
  const events = await observations(fetcher);
  expect(count(events, 'organization_member_remove', 'success')).toBe(0);
  expect(count(events, 'organization_member_remove', 'use')).toBe(1);
});

it('records an admitted self-removal before leaving its Organization route', async () => {
  const myself = { ...member, userId: actor, role: 1 };
  const owner = { ...member, membershipId: '66666666-6666-4666-8666-666666666666', email: 'another-owner@example.test',
    displayName: 'Another Owner', role: 0, isUsableOwner: true };
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => path === '/me/activity-client-events' ? reply(undefined, 204)
    : path === '/me' ? reply({ id: actor }) : options?.method === 'DELETE' ? reply(undefined, 204)
      : reply(path === root ? { ...directory, items: [myself, owner], actorRole: 1 } : { ...review, member: myself, actorRole: 1 }));
  vi.stubGlobal('fetch', fetcher); mount(); await openReview();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member removal' })); await screen.findByText('Organizations destination');
  const events = await observations(fetcher);
  expect(count(events, 'organization_member_remove', 'success')).toBe(1);
  expect(count(events, 'organization_member_remove', 'failure')).toBe(0);
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
});
it('preserves the acknowledged removal when the optional report transport fails', async () => {
  const fetcher = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') throw new Error('Private diagnostic transport failure');
    return path === '/me' ? reply({ id: actor }) : options?.method === 'DELETE' ? reply(undefined, 204) : reply(path === root ? directory : review);
  }); vi.stubGlobal('fetch', fetcher); mount(); await openReview();
  fireEvent.click(screen.getByRole('button', { name: 'Confirm member removal' })); await screen.findByText('Member removed.');
  const events = await observations(fetcher);
  expect(count(events, 'organization_member_remove', 'success')).toBe(1);
  expect(screen.getByText('Member removed.')).toBeVisible();
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  expect(fetcher.mock.calls.filter(call => call[0] === '/me/activity-client-events')).toHaveLength(1);
});
