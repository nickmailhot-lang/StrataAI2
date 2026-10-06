import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationsPage } from './InvitationsPage';

const invitation = { id: '11111111-1111-1111-1111-111111111111', organizationId: '22222222-2222-2222-2222-222222222222', organizationName: 'Council', surface: 'PORTAL', targetRole: 'OWNER', expiresAt: '2026-10-04T00:00:00Z' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const actor = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const otherActor = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
// Existing response queues describe invitation IO; account checks are separate.
function stableFetch(fetcher: typeof fetch) {
  vi.stubGlobal('fetch', (...args: Parameters<typeof fetch>) => String(args[0]) === '/me'
    ? Promise.resolve(reply({ id: actor })) : fetcher(...args));
}
function mount() {
  const router = createMemoryRouter([{ path: '/app/invitations', element: <InvitationsPage /> }, { path: '/login', element: <h1>Sign in destination</h1> }], { initialEntries: ['/app/invitations'] });
  const view = render(<RouterProvider router={router} />);
  return { ...view, router };
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
describe('PRD-60 verified email invitation discovery', () => {
  it('clears old invitation labels during a refresh and after failure until a fresh read succeeds', async () => {
    let finish: ((response: Response) => void) | undefined;
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [boardInvitation], nextCursor: null }))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(reply({ items: [boardInvitation], nextCursor: null }));
    stableFetch( fetcher); mount();
    await screen.findByRole('heading', { name: 'Maintenance' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    expect(screen.queryByRole('heading', { name: 'Maintenance' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Council' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Accept invitation to/ })).not.toBeInTheDocument();
    await waitFor(() => expect(finish).toBeDefined());
    await act(async () => { finish!(reply({}, 503)); });
    await screen.findByText('Unable to load invitations. Please refresh and try again.');
    expect(screen.queryByRole('heading', { name: 'Maintenance' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Accept invitation to/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    await screen.findByRole('heading', { name: 'Maintenance' });
    expect(fetcher.mock.calls.every(call => call[1].method === 'GET')).toBe(true);
  });
  it('keeps only generic recovery after an uncertain acceptance and a failed discovery refresh', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [boardInvitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('Lost acknowledgment')).mockResolvedValueOnce(reply({}, 503))
      .mockResolvedValueOnce(reply(boardAcknowledgment));
    stableFetch( fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council, Board Maintenance, admin' }));
    await screen.findByRole('button', { name: 'Retry invitation acceptance' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    await screen.findByText('Unable to load invitations. Please refresh and try again.');
    expect(screen.queryByRole('heading', { name: 'Maintenance' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Council' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
    await screen.findByRole('link', { name: 'Open Board' });
    expect(fetcher.mock.calls[3][0]).toBe(fetcher.mock.calls[1][0]);
    expect(fetcher.mock.calls.filter(call => call[1].method === 'POST')).toHaveLength(2);
  });
  it('preserves an unconfirmed acceptance after refresh removes the committed invitation', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('Lost committed response'))
      .mockResolvedValueOnce(reply({ items: [], nextCursor: null }))
      .mockResolvedValueOnce(reply({ invitationId: invitation.id, organizationId: invitation.organizationId, surface: invitation.surface, targetRole: invitation.targetRole }));
    stableFetch( fetcher); mount();
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
    stableFetch( fetcher); mount();
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
    stableFetch( fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    await screen.findByRole('button', { name: 'Retry invitation acceptance' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
    await screen.findByRole('heading', { name: 'Sign in destination' });
    expect(screen.queryByRole('button', { name: 'Retry invitation acceptance' })).not.toBeInTheDocument();
    expect(screen.queryByText('Council')).not.toBeInTheDocument();
  });
  it('shows an empty state and explicit refresh', async () => {
    stableFetch( vi.fn().mockResolvedValue(reply({ items: [], nextCursor: null })));
    mount();
    expect(await screen.findByText('No pending invitations on this page.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Refresh invitations' })).toBeEnabled();
  });
  it('accepts portal access with its matching acknowledgment and separate destination', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockResolvedValueOnce(reply({ invitationId: invitation.id, organizationId: invitation.organizationId, surface: invitation.surface, targetRole: invitation.targetRole }));
    stableFetch( fetcher); mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Accept invitation to Council' }));
    expect(await screen.findByRole('link', { name: 'Open Owner Portal' })).toHaveAttribute('href', `/portal/${invitation.organizationId}`);
    expect(screen.queryByRole('button', { name: 'Accept invitation to Council' })).not.toBeInTheDocument();
    expect(fetcher.mock.calls[1][0]).toBe(`/me/invitations/${invitation.id}/accept?expectedActorId=${actor}`);
    expect(fetcher.mock.calls[1][1].method).toBe('POST');
  });
  it('retains the same invitation after an uncertain acceptance and rejects a wrong acknowledgment', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [invitation], nextCursor: null }))
      .mockRejectedValueOnce(new Error('disconnect')).mockResolvedValueOnce(reply({ invitationId: invitation.id, organizationId: '33333333-3333-3333-3333-333333333333', surface: 'PORTAL', targetRole: 'OWNER' }));
    stableFetch( fetcher); mount();
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
    stableFetch( vi.fn().mockImplementation(() => new Promise<Response>(resolve => { complete = resolve; })));
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
    stableFetch( fetcher); mount();
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
  stableFetch( fetcher); mount();
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
  stableFetch( vi.fn().mockResolvedValueOnce(reply({ items: [boardInvitation], nextCursor: null })).mockResolvedValueOnce(reply(ack)));
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
  stableFetch( vi.fn().mockResolvedValue(reply({ items: [item], nextCursor: null }))); mount();
  await screen.findByText('Unable to load invitations. Please refresh and try again.');
  expect(screen.queryByRole('heading', { name: 'Council' })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Maintenance' })).not.toBeInTheDocument();
});

it('advances a filtered empty candidate page by its scan cursor', async () => {
  const cursor = '44444444-4444-4444-8444-444444444444';
  const fetcher = vi.fn().mockResolvedValueOnce(reply({ items: [], nextCursor: cursor }))
    .mockResolvedValueOnce(reply({ items: [], nextCursor: null }));
  stableFetch( fetcher); mount();
  fireEvent.click(await screen.findByRole('button', { name: 'More invitations' }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2));
  expect(fetcher.mock.calls[1][0]).toBe(`/me/invitations?expectedActorId=${actor}&after=${cursor}`);
  await waitFor(() => expect(screen.queryByRole('button', { name: 'More invitations' })).not.toBeInTheDocument());
});
it('rejects a cursor that goes backward after an authorization-filtered page', async () => {
  const cursor = '44444444-4444-4444-8444-444444444444';
  stableFetch( vi.fn().mockResolvedValueOnce(reply({ items: [], nextCursor: cursor }))
    .mockResolvedValueOnce(reply({ items: [], nextCursor: invitation.id })));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'More invitations' }));
  await screen.findByText('Unable to load invitations. Please refresh and try again.');
});

const internalInvitation = { ...invitation, surface: 'INTERNAL', targetRole: 'MEMBER' };
it.each([invitation, internalInvitation, boardInvitation].flatMap(item => ['before', 'after'].map(phase => ({ item, phase }))))(
  'binds $item.surface acceptance to its reviewer when the account changes $phase the command', async ({ item, phase }) => {
    let profiles = 0; const commands: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit) => {
      if (input === '/me') return reply({ id: ++profiles === (phase === 'before' ? 3 : 4) ? otherActor : actor });
      if (init.method === 'POST') {
        commands.push(input);
        return reply({ invitationId: item.id, organizationId: item.organizationId, surface: item.surface,
          targetRole: item.targetRole, boardTarget: 'boardTarget' in item ? item.boardTarget : undefined });
      }
      expect(new URL(input, 'https://test').searchParams.get('expectedActorId')).toBe(actor);
      return reply({ items: [item], nextCursor: null });
    }));
    mount(); fireEvent.click(await screen.findByRole('button', { name: /^Accept invitation to/ }));
    await screen.findByRole('heading', { name: 'Sign in destination' });
    expect(commands).toHaveLength(phase === 'before' ? 0 : 1);
    if (commands.length) expect(commands[0]).toBe(`/me/invitations/${item.id}/accept?expectedActorId=${actor}`);
    expect(screen.queryByRole('link', { name: /^Open / })).not.toBeInTheDocument();
    expect(screen.queryByText('Council')).not.toBeInTheDocument();
    expect(screen.queryByText('Maintenance')).not.toBeInTheDocument();
  });
it('withholds a completed discovery page if the account changes while it is read', async () => {
  let profiles = 0;
  vi.stubGlobal('fetch', vi.fn(async (input: string) => input === '/me'
    ? reply({ id: ++profiles === 1 ? actor : otherActor }) : reply({ items: [boardInvitation], nextCursor: null })));
  mount(); await screen.findByRole('heading', { name: 'Sign in destination' });
  expect(screen.queryByText('Council')).not.toBeInTheDocument(); expect(screen.queryByText('Maintenance')).not.toBeInTheDocument();
});
it.each([invitation, internalInvitation, boardInvitation])('holds a committed $surface attempt until the original account is freshly confirmed', async item => {
  let profiles = 0; const commands: string[] = []; let accepted = false;
  vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit) => {
    if (input === '/me') return ++profiles === 4 ? reply({}, 503) : reply({ id: actor });
    if (init.method === 'POST') {
      accepted = true; commands.push(input);
      return reply({ invitationId: item.id, organizationId: item.organizationId, surface: item.surface,
        targetRole: item.targetRole, boardTarget: 'boardTarget' in item ? item.boardTarget : undefined });
    }
    return reply({ items: accepted ? [] : [item], nextCursor: null });
  }));
  mount(); fireEvent.click(await screen.findByRole('button', { name: /^Accept invitation to/ }));
  await screen.findByText('Unable to confirm the reviewed account. Refresh invitations before continuing.');
  expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeDisabled();
  expect(screen.queryByText('Council')).not.toBeInTheDocument(); expect(screen.queryByText('Maintenance')).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /^Open / })).not.toBeInTheDocument(); expect(commands).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled());
  expect(commands).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
  await screen.findByRole('link', { name: /^Open / });
  expect(commands).toHaveLength(2); expect(commands[1]).toBe(commands[0]);
});
it('refuses malformed account confirmation before publishing any invitation labels', async () => {
  const fetcher = vi.fn().mockResolvedValue(reply({ id: 'invalid' })); vi.stubGlobal('fetch', fetcher);
  mount(); await screen.findByText('Unable to confirm the reviewed account. Refresh invitations before continuing.');
  expect(fetcher).toHaveBeenCalledTimes(1); expect(screen.queryByRole('button', { name: /^Accept invitation to/ })).not.toBeInTheDocument();
});

it.each(['before', 'after'])('bounds account confirmation %s acceptance and fences a late successful response', async phase => {
  let profiles = 0; let late: ((value: Response) => void) | undefined; let signal: AbortSignal | undefined;
  const commands: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit) => {
    if (input === '/me') {
      if (++profiles === (phase === 'before' ? 3 : 4)) {
        signal = init.signal as AbortSignal;
        return await new Promise<Response>(resolve => { late = resolve; });
      }
      return reply({ id: actor });
    }
    if (init.method === 'POST') {
      commands.push(input); return reply({ invitationId: invitation.id, organizationId: invitation.organizationId,
        surface: invitation.surface, targetRole: invitation.targetRole });
    }
    return reply({ items: [invitation], nextCursor: null });
  }));
  mount(); const button = await screen.findByRole('button', { name: 'Accept invitation to Council' });
  vi.useFakeTimers(); fireEvent.click(button);
  await act(async () => { await vi.advanceTimersByTimeAsync(16_000); });
  expect(signal?.aborted).toBe(true);
  expect(screen.getByText('Unable to confirm the reviewed account. Refresh invitations before continuing.')).toBeVisible();
  expect(commands).toHaveLength(phase === 'before' ? 0 : 1);
  expect(screen.queryByText('Council')).not.toBeInTheDocument();
  if (phase === 'after') expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeDisabled();
  else expect(screen.queryByRole('button', { name: 'Retry invitation acceptance' })).not.toBeInTheDocument();
  vi.useRealTimers(); await act(async () => { late!(reply({ id: actor })); });
  expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  expect(screen.queryByText('Council')).not.toBeInTheDocument(); expect(commands).toHaveLength(phase === 'before' ? 0 : 1);
});
