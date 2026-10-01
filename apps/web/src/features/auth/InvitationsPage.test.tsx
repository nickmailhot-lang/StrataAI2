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
