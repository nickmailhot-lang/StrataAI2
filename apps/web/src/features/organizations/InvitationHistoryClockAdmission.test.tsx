import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardInvitationHistoryPage, OrganizationInvitationHistoryPage } from './OrganizationInvitationHistoryPage';

vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: vi.fn(() => vi.fn()) }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn((options: { status(value: string): void }) => { options.status('live'); return vi.fn(); }) }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-4111-8111-111111111111', board = '22222222-2222-4222-8222-222222222222';
const actor = '33333333-3333-4333-8333-333333333333';
const profile = { id: actor, locale: 'en-CA', timezone: 'UTC' };
const row = { id: '44444444-4444-4444-8444-444444444444', email: 'private-clock-recipient@example.test', surface: 'INTERNAL', targetRole: 'MEMBER',
  createdAt: '2034-01-01T00:00:00Z', expiresAt: '2035-01-08T18:00:00Z', acceptedAt: null, revokedAt: null, deliveryState: 'SENT' };
const invalid = [
  ['createdAt', '2035-02-30T12:00:00Z'], ['expiresAt', '2035-02-29T12:00:00+05:30'],
  ['acceptedAt', '2035-04-31T12:00:00-07:00'], ['revokedAt', '2035-03-08T24:00:00Z'],
] as const;
const response = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
afterEach(() => vi.unstubAllGlobals());
for (const boardSurface of [false, true]) {
  function setup(field: string, value: string) {
    let malformed = true; let writes = 0;
    const root = boardSurface ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
    const validRow = { ...row, ...(boardSurface ? { boardTarget: { boardId: board, role: 'MEMBER' } } : {}) };
    vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
      if (options?.method === 'DELETE') { writes++; throw new Error('Unexpected revocation'); }
      if (path === '/me') return response(profile);
      if (path === `/boards/${board}`) return response({ board: { id: board, organizationId: org, lifecycleState: 'active', name: 'Private clock Board' }, access: { canAdminister: true } });
      if (path === root) return response({ items: [{ ...validRow, ...(malformed ? { [field]: value } : {}) }], nextCursor: null });
      throw new Error('Unexpected history request');
    }));
    const path = boardSurface ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`;
    render(<RouterProvider router={createMemoryRouter([{ path: boardSurface ? '/app/:organizationId/boards/:boardId/invitations' : '/app/:organizationId/invitations',
      element: boardSurface ? <BoardInvitationHistoryPage /> : <OrganizationInvitationHistoryPage /> }], { initialEntries: [path] })} />);
    return { recover: () => { malformed = false; }, writes: () => writes };
  }
  it.each(invalid)('withholds malformed %s and requires a fresh valid history read (Board=' + boardSurface + ')', async (field, value) => {
    const state = setup(field, value);
    await screen.findByText('Unable to confirm invitation history. Please retry.');
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Private clock Board')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: `Revoke invitation for ${row.email}` })).not.toBeInTheDocument(); expect(state.writes()).toBe(0);
    const refresh = screen.getByRole('button', { name: 'Refresh invitations' }); await waitFor(() => expect(refresh).toBeEnabled());
    state.recover(); fireEvent.click(refresh); await screen.findByRole('heading', { name: row.email });
    expect(screen.getByRole('button', { name: `Revoke invitation for ${row.email}` })).toBeEnabled(); expect(state.writes()).toBe(0);
  });
  it.each(invalid.map(([field]) => field))('retains a real leap-day source calendar and seven-digit offset precision in %s (Board=' + boardSurface + ')', async field => {
    const state = setup(field, '2036-02-29T00:30:00.1234567+14:00');
    await screen.findByRole('heading', { name: row.email });
    expect(screen.queryByText('Unable to confirm invitation history. Please retry.')).not.toBeInTheDocument(); expect(state.writes()).toBe(0);
  });
}
