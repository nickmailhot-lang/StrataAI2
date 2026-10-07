import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationsPage } from './InvitationsPage';
import { watchInvitationRecipient, type InvitationRecipientInvalidation } from './invitationRecipientLive';
vi.mock('./invitationRecipientLive', () => ({ watchInvitationRecipient: vi.fn() }));
const actor = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const item = { id: '11111111-1111-4111-8111-111111111111', organizationId: '22222222-2222-4222-8222-222222222222',
  organizationName: 'Private Council', surface: 'PORTAL', targetRole: 'OWNER', expiresAt: '2035-01-08T18:00:00Z' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
let invalidate: (reason: InvitationRecipientInvalidation) => void; let stop: ReturnType<typeof vi.fn<() => void>>;
beforeEach(() => {
  stop = vi.fn();
  vi.mocked(watchInvitationRecipient).mockReset();
  vi.mocked(watchInvitationRecipient).mockImplementation(options => { invalidate = options.invalidate; return stop; });
});
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
function mount() {
  const router = createMemoryRouter([{ path: '/app/invitations', element: <InvitationsPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> }], { initialEntries: ['/app/invitations'] });
  return render(<RouterProvider router={router} />);
}
function stable(fetcher: typeof fetch) {
  vi.stubGlobal('fetch', (...args: Parameters<typeof fetch>) => String(args[0]) === '/me'
    ? Promise.resolve(reply({ id: actor })) : fetcher(...args));
}
async function bootstrap() {
  await waitFor(() => expect(watchInvitationRecipient).toHaveBeenCalledWith(expect.objectContaining({ subject: actor })));
  await act(async () => invalidate('reset'));
}
it('captures the protected stream head before discovery and announces without moving focus', async () => {
  const fetcher = vi.fn().mockResolvedValue(reply({ items: [item], nextCursor: null })); stable(fetcher);
  const view = mount(); expect(fetcher).not.toHaveBeenCalled();
  const refresh = screen.getByRole('button', { name: 'Refresh invitations' }); refresh.focus();
  fireEvent.click(refresh); expect(fetcher).not.toHaveBeenCalled();
  await bootstrap(); await screen.findByRole('heading', { name: item.organizationName });
  expect(fetcher).toHaveBeenCalledTimes(1); expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');
  expect(refresh).toHaveFocus(); view.unmount(); expect(stop).toHaveBeenCalledTimes(1);
});
it('withdraws old labels on live change, fences an obsolete read and never submits during automatic recovery', async () => {
  let late!: (response: Response) => void; const fetcher = vi.fn()
    .mockResolvedValueOnce(reply({ items: [item], nextCursor: null }))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { late = resolve; }))
    .mockResolvedValue(reply({ items: [], nextCursor: null })); stable(fetcher); mount();
  await bootstrap(); await screen.findByRole('heading', { name: item.organizationName });
  await act(async () => invalidate('change'));
  expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /^Accept invitation/ })).not.toBeInTheDocument();
  await act(async () => invalidate('change')); await screen.findByText('No pending invitations on this page.');
  await act(async () => late(reply({ items: [item], nextCursor: null })));
  expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
  expect(fetcher.mock.calls.every(call => call[1].method === 'GET')).toBe(true);
});
it('moves focus to the stable refresh control only when a withdrawn invitation owned focus', async () => {
  stable(vi.fn().mockResolvedValueOnce(reply({ items: [item], nextCursor: null }))
    .mockResolvedValue(reply({ items: [], nextCursor: null })));
  mount(); await bootstrap();
  const accept = await screen.findByRole('button', { name: /^Accept invitation to/ }); accept.focus();
  await act(async () => invalidate('change')); await screen.findByText('No pending invitations on this page.');
  expect(screen.getByRole('button', { name: 'Refresh invitations' })).toHaveFocus();
});
it('withdraws unsent consent on live change during account admission without inventing an uncertain command', async () => {
  let profiles = 0; let late!: (response: Response) => void; const commands: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit) => {
    if (input === '/me') {
      if (++profiles === 3) return new Promise<Response>(resolve => { late = resolve; });
      return reply({ id: actor });
    }
    if (init.method === 'POST') commands.push(input);
    return reply({ items: profiles >= 4 ? [] : [item], nextCursor: null });
  }));
  mount(); await bootstrap();
  fireEvent.click(await screen.findByRole('button', { name: /^Accept invitation to/ }));
  await waitFor(() => expect(profiles).toBe(3)); await act(async () => invalidate('change'));
  await screen.findByText('No pending invitations on this page.');
  await act(async () => late(reply({ id: actor })));
  expect(commands).toHaveLength(0); expect(screen.queryByRole('button', { name: 'Retry invitation acceptance' })).not.toBeInTheDocument();
});
it('preserves only explicit original-ID recovery when a live event precedes a committed acknowledgment', async () => {
  let late!: (response: Response) => void; const writes: string[] = [];
  const ack = { invitationId: item.id, organizationId: item.organizationId, surface: item.surface, targetRole: item.targetRole };
  stable(vi.fn(async (input: string, init: RequestInit) => {
    if (init.method === 'POST') {
      writes.push(input);
      if (writes.length === 1) return new Promise<Response>(resolve => { late = resolve; });
      return reply(ack);
    }
    return reply({ items: writes.length ? [] : [item], nextCursor: null });
  }) as typeof fetch);
  mount(); await bootstrap();
  fireEvent.click(await screen.findByRole('button', { name: /^Accept invitation to/ }));
  await waitFor(() => expect(writes).toHaveLength(1)); await act(async () => invalidate('change'));
  const retry = await screen.findByRole('button', { name: 'Retry invitation acceptance' }); await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument(); expect(writes).toHaveLength(1);
  await act(async () => late(reply(ack))); expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByRole('link', { name: 'Open Owner Portal' });
  expect(writes).toHaveLength(2); expect(writes[1]).toBe(writes[0]);
});
it('withdraws and returns to sign-in if replay recovery finds a replacement account', async () => {
  let changed = false;
  vi.stubGlobal('fetch', vi.fn(async (input: string) => input === '/me'
    ? reply({ id: changed ? item.id : actor }) : reply({ items: [item], nextCursor: null })));
  mount(); await bootstrap(); await screen.findByRole('heading', { name: item.organizationName });
  changed = true; await act(async () => invalidate('reconnecting')); await screen.findByRole('heading', { name: 'Sign in destination' });
  expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument(); expect(stop).toHaveBeenCalledTimes(1);
});
it('bounds missing bootstrap and coalesces repeated transport failures without interrupting protected recovery', async () => {
  vi.useFakeTimers(); let late!: (response: Response) => void;
  const fetcher = vi.fn(() => new Promise<Response>(resolve => { late = resolve; })); stable(fetcher);
  let view!: ReturnType<typeof mount>; await act(async () => { view = mount(); });
  await act(async () => vi.advanceTimersByTimeAsync(15_000));
  expect(fetcher).toHaveBeenCalledTimes(1); await act(async () => { invalidate('unavailable'); invalidate('unavailable'); });
  expect(fetcher).toHaveBeenCalledTimes(1);
  await act(async () => late(reply({ items: [], nextCursor: null })));
  expect(screen.getByText('No pending invitations on this page.')).toBeVisible();
  view.unmount(); await act(async () => { invalidate('change'); await vi.advanceTimersByTimeAsync(30_000); });
  expect(fetcher).toHaveBeenCalledTimes(1); expect(stop).toHaveBeenCalledTimes(1);
});
it('binds the socket and first protected discovery to the account captured before bootstrap', async () => {
  let profiles = 0; const requests: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: string) => {
    requests.push(input);
    if (input === '/me') return reply({ id: ++profiles === 1 ? actor : item.id });
    expect(new URL(input, 'https://test').searchParams.get('expectedActorId')).toBe(actor);
    return reply({}, 401);
  }));
  mount(); await bootstrap(); await screen.findByRole('heading', { name: 'Sign in destination' });
  expect(requests).toHaveLength(3); expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
});
it('bounds initial account JSON and never opens a socket or invitation read after late admission', async () => {
  vi.useFakeTimers(); let complete!: (value: unknown) => void;
  const fetcher = vi.fn().mockResolvedValue({ ok: true, status: 200,
    json: () => new Promise<unknown>(resolve => { complete = resolve; }) } as Response);
  vi.stubGlobal('fetch', fetcher); await act(async () => { mount(); });
  await act(async () => vi.advanceTimersByTimeAsync(15_000));
  expect(screen.getByText('Unable to confirm the reviewed account. Refresh invitations before continuing.')).toBeVisible();
  expect(watchInvitationRecipient).not.toHaveBeenCalled(); expect(fetcher).toHaveBeenCalledTimes(1);
  await act(async () => complete({ id: actor }));
  expect(watchInvitationRecipient).not.toHaveBeenCalled(); expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
});
it('restarts account admission and captured-head discovery after an initial failure without any mutation', async () => {
  let profiles = 0; let discover = false; const reads: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: string, init: RequestInit) => {
    expect(init.method).toBe('GET'); reads.push(input);
    if (input === '/me') return ++profiles === 1 ? reply({}, 503) : reply({ id: actor });
    return reply({ items: discover ? [item] : [], nextCursor: null });
  }));
  mount(); await screen.findByText('Unable to confirm the reviewed account. Refresh invitations before continuing.');
  expect(watchInvitationRecipient).not.toHaveBeenCalled();
  const refresh = screen.getByRole('button', { name: 'Refresh invitations' }); refresh.focus(); fireEvent.click(refresh);
  await waitFor(() => expect(watchInvitationRecipient).toHaveBeenCalledTimes(1));
  expect(reads).toEqual(['/me', '/me']); expect(refresh).toHaveFocus();
  await act(async () => invalidate('reset')); await screen.findByText('No pending invitations on this page.');
  discover = true; await act(async () => invalidate('change')); await screen.findByRole('heading', { name: item.organizationName });
  expect(watchInvitationRecipient).toHaveBeenCalledTimes(1); expect(refresh).toHaveFocus();
});
it('fresh admission retry stays bounded and ignores its late profile after another failure', async () => {
  vi.useFakeTimers(); let profiles = 0; let late!: (value: unknown) => void;
  vi.stubGlobal('fetch', vi.fn(async (input: string) => {
    expect(input).toBe('/me');
    if (++profiles === 1) return reply({}, 503);
    return { ok: true, status: 200, json: () => new Promise<unknown>(resolve => { late = resolve; }) } as Response;
  }));
  await act(async () => { mount(); });
  expect(screen.getByText('Unable to confirm the reviewed account. Refresh invitations before continuing.')).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Refresh invitations' }));
  await act(async () => vi.advanceTimersByTimeAsync(15_000));
  expect(screen.getByText('Unable to confirm the reviewed account. Refresh invitations before continuing.')).toBeVisible();
  await act(async () => late({ id: actor })); expect(watchInvitationRecipient).not.toHaveBeenCalled(); expect(profiles).toBe(2);
});
