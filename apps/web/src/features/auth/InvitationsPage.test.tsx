import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationsPage } from './InvitationsPage';

const invitation = { id: '11111111-1111-1111-1111-111111111111', organizationId: '22222222-2222-2222-2222-222222222222', organizationName: 'Council', surface: 'PORTAL', targetRole: 'OWNER', expiresAt: '2026-10-04T00:00:00Z' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount() {
  const router = createMemoryRouter([{ path: '/app/invitations', element: <InvitationsPage /> }, { path: '/login', element: <h1>Sign in destination</h1> }], { initialEntries: ['/app/invitations'] });
  const view = render(<RouterProvider router={router} />);
  return { ...view, router };
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
describe('PRD-60 verified email invitation discovery', () => {
  it('preserves an unconfirmed acceptance after refresh removes the committed invitation', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('Lost committed response'))
      .mockResolvedValueOnce(reply({ items: [], nextCursor: null }))
      .mockResolvedValueOnce(reply({ invitationId: invitation.id, organizationId: invitation.organizationId, surface: invitation.surface, targetRole: invitation.targetRole }));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    await screen.findByRole('button', { name: 'Retry invitation acceptance' });
    expect(screen.queryByRole('heading', { name: 'Council' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled());
    expect(fetcher).toHaveBeenCalledTimes(3);
    fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
    expect(await screen.findByRole('link', { name: 'Open Owner Portal' })).toHaveAttribute('href', `/portal/${invitation.organizationId}`);
    expect(fetcher.mock.calls[3][0]).toBe(fetcher.mock.calls[1][0]);
    expect(screen.queryByRole('button', { name: 'Retry invitation acceptance' })).not.toBeInTheDocument();
  });
  it('clears an unconfirmed intent when current authorization denies its retry', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('Uncertain acceptance')).mockResolvedValueOnce(reply({}, 403));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Retry invitation acceptance' }));
    await screen.findByText('This invitation is no longer available to your account. Refresh to check current invitations.');
    expect(screen.queryByRole('button', { name: 'Retry invitation acceptance' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Council' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  });
  it('drops pending acceptance details when a refreshed session is revoked', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('Uncertain acceptance')).mockResolvedValueOnce(reply({}, 401));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    await screen.findByRole('button', { name: 'Retry invitation acceptance' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    await screen.findByRole('heading', { name: 'Sign in destination' });
    expect(screen.queryByRole('button', { name: 'Retry invitation acceptance' })).not.toBeInTheDocument();
    expect(screen.queryByText('Council')).not.toBeInTheDocument();
  });
  it('shows an empty state and explicit refresh', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ items: [], nextCursor: null })));
    mount();
    expect(await screen.findByText('No pending invitations on this page.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Refresh invitations' })).toBeEnabled();
  });
  it('accepts portal access with its matching acknowledgment and separate destination', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockResolvedValueOnce(reply({ invitationId: invitation.id, organizationId: invitation.organizationId, surface: invitation.surface, targetRole: invitation.targetRole }));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    expect(await screen.findByRole('link', { name: 'Open Owner Portal' })).toHaveAttribute('href', `/portal/${invitation.organizationId}`);
    expect(screen.queryByRole('button', { name: 'Accept invitation to Council' })).not.toBeInTheDocument();
    expect(fetcher.mock.calls[1][0]).toBe(`/me/invitations/${invitation.id}/accept`);
    expect(fetcher.mock.calls[1][1].method).toBe('POST');
  });
  it('retains the same invitation after an uncertain acceptance and rejects a wrong acknowledgment', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('disconnect')).mockResolvedValueOnce(reply({ invitationId: invitation.id, organizationId: '33333333-3333-3333-3333-333333333333', surface: 'PORTAL', targetRole: 'OWNER' }));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    await screen.findByText('Unable to confirm acceptance. You can retry this invitation safely.');
    fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
    await act(async () => { await Promise.resolve(); });
    expect(fetcher.mock.calls[2][0]).toBe(fetcher.mock.calls[1][0]);
    expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled();
  });
  it('bounds a stalled read and fences completion after unmount', async () => {
    vi.useFakeTimers(); let complete: ((response: Response) => void) | undefined;
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise<Response>(resolve => { complete = resolve; })));
    const view = mount();
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(screen.getByText('Unable to load invitations. Please refresh and try again.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Refresh invitations' })).toBeEnabled();
    view.unmount();
    await act(async () => { complete?.(reply({ items: [invitation], nextCursor: null })); });
    expect(view.router.state.location.pathname).toBe('/app/invitations');
  });
  it('denies malformed pages and routes revoked sessions to sign-in', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation, invitation], nextCursor: null })).mockResolvedValueOnce(reply({}, 401));
    vi.stubGlobal('fetch', fetcher); mount();
    await screen.findByText('Unable to load invitations. Please refresh and try again.');
    expect(screen.queryByRole('button', { name: 'Accept invitation to Council' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    expect(await screen.findByRole('heading', { name: 'Sign in destination' })).toBeVisible();
  });
});

const boardInvitation = { ...invitation, surface: 'INTERNAL', targetRole: 'MEMBER', boardName: 'Maintenance',
  boardTarget: { boardId: '33333333-3333-4333-8333-333333333333', role: 'ADMIN' } };
const boardAcknowledgment = { invitationId: invitation.id, organizationId: invitation.organizationId,
  surface: 'INTERNAL', targetRole: 'MEMBER', boardTarget: boardInvitation.boardTarget };
it('displays the Board role and preserves its exact target across lost acknowledgment and refresh', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [boardInvitation], nextCursor: null }))
    .mockRejectedValueOnce(new Error('Lost acknowledgment')).mockResolvedValueOnce(reply({ items: [], nextCursor: null }))
    .mockResolvedValueOnce(reply(boardAcknowledgment));
  vi.stubGlobal('fetch', fetcher); mount();
  await screen.findByRole('heading', { name: 'Maintenance' });
  expect(screen.getByText('Board access \u00b7 admin')).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Accept invitation to Council, Board Maintenance, admin' }));
  await screen.findByRole('button', { name: 'Retry invitation acceptance' });
  fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
  expect(await screen.findByRole('link', { name: 'Open Board' })).toHaveAttribute('href', `/app/${invitation.organizationId}/boards/${boardInvitation.boardTarget.boardId}`);
  expect(fetcher.mock.calls[3][0]).toBe(fetcher.mock.calls[1][0]);
});
it.each([
  { ...boardAcknowledgment, boardTarget: null },
  { ...boardAcknowledgment, boardTarget: { ...boardInvitation.boardTarget, boardId: invitation.id } },
  { ...boardAcknowledgment, boardTarget: { ...boardInvitation.boardTarget, role: 'MEMBER' } },
])('rejects a Board acknowledgment that differs from the displayed invitation: %j', async ack => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ items: [boardInvitation], nextCursor: null })).mockResolvedValueOnce(reply(ack)));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council, Board Maintenance, admin' }));
  await screen.findByRole('button', { name: 'Retry invitation acceptance' });
  expect(screen.queryByRole('link', { name: 'Open Board' })).not.toBeInTheDocument();
});
it.each([
  { ...boardInvitation, surface: 'PORTAL', targetRole: 'OWNER' },
  { ...boardInvitation, boardName: null },
  { ...boardInvitation, boardTarget: { ...boardInvitation.boardTarget, role: 'OWNER' } },
  { ...boardInvitation, boardTarget: { ...boardInvitation.boardTarget, boardId: '00000000-0000-0000-0000-000000000000' } },
  { ...invitation, targetRole: 'ADMIN' },
])('rejects unsupported invitation metadata before disclosure: %j', async item => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ items: [item], nextCursor: null }))); mount();
  await screen.findByText('Unable to load invitations. Please refresh and try again.');
  expect(screen.queryByRole('heading', { name: 'Council' })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Maintenance' })).not.toBeInTheDocument();
});
