import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { BoardInvitationHistoryPage } from './OrganizationInvitationHistoryPage';

vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: vi.fn(() => vi.fn()) }));
vi.mock('../../api/boardLive', () => ({ watchBoard: vi.fn((options: { status(value: string): void }) => { options.status('live'); return vi.fn(); }) }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const org = '11111111-1111-4111-8111-111111111111'; const board = '22222222-2222-4222-8222-222222222222';
const actor = '33333333-3333-4333-8333-333333333333';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const admission = { board: { id: board, organizationId: org, name: 'Private history Board', lifecycleState: 'active' }, access: { canAdminister: true } };
const row = { id: '44444444-4444-4444-8444-444444444444', email: 'private-history-recipient@example.test', surface: 'INTERNAL', targetRole: 'MEMBER',
  createdAt: '2026-10-16T00:00:00Z', expiresAt: '2026-12-01T00:00:00Z', acceptedAt: null, revokedAt: null, deliveryState: 'SENT', boardTarget: { boardId: board, role: 'MEMBER' } };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount() {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/boards/:boardId/invitations', element: <BoardInvitationHistoryPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
  ], { initialEntries: [`/app/${org}/boards/${board}/invitations`] })} />);
}
afterEach(() => vi.unstubAllGlobals());

it.each([429, 500, 502, 503])('requires fresh Board admission after temporary history-scope failure %s before reading invitations', async status => {
  let scopes = 0; let history = 0; let writes = 0;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (init?.method === 'DELETE') { writes++; return response({}, 409); }
    if (path === '/me') return response(profile);
    if (path === `/boards/${board}`) return ++scopes === 1 ? response({ title: 'Private server failure', board: admission.board }, status) : response(admission);
    if (path === `/boards/${board}/invitations`) { history++; return response({ items: [row], nextCursor: null }); }
    throw new Error('Unexpected history recovery request');
  }); vi.stubGlobal('fetch', fetcher); mount();
  await screen.findByText('Unable to confirm invitation history. Please retry.');
  const retry = screen.getByRole('button', { name: 'Refresh invitations' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(history).toBe(0); expect(writes).toBe(0);
  for (const value of [admission.board.name, row.email, 'Private server failure']) expect(screen.queryByText(value)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: `Revoke invitation for ${row.email}` })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByRole('heading', { name: row.email });
  expect(scopes).toBe(2); expect(history).toBe(1); expect(writes).toBe(0);
  expect(screen.getByText(admission.board.name)).toBeVisible();
  expect(screen.getByRole('button', { name: `Revoke invitation for ${row.email}` })).toBeEnabled();
});

it('still withholds history and revocation when a temporary failure recovers to a nonadministrative Board scope', async () => {
  let scopes = 0; let history = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string, _init?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (path === `/boards/${board}`) return ++scopes === 1 ? response({}, 503) : response({ ...admission, access: { canAdminister: false } });
    history++; return response({ items: [row], nextCursor: null });
  })); mount(); await screen.findByText('Unable to confirm invitation history. Please retry.');
  const retry = screen.getByRole('button', { name: 'Refresh invitations' }); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
  await screen.findByRole('link', { name: 'Sign in' });
  expect(history).toBe(0); expect(screen.queryByText(admission.board.name)).not.toBeInTheDocument(); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Refresh invitations' })).not.toBeInTheDocument();
});
