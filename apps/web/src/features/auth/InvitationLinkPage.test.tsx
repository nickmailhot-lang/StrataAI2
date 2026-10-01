import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationLinkPage } from './InvitationLinkPage';

const token = 'private_invitation_proof_12345678901234567890';
const item = { id: '11111111-1111-4111-8111-111111111111', organizationId: '22222222-2222-4222-8222-222222222222', organizationName: 'Invited council', surface: 'PORTAL', targetRole: 'OWNER', expiresAt: '2035-01-01T00:00:00Z' };
const ack = { invitationId: item.id, organizationId: item.organizationId, surface: item.surface, targetRole: item.targetRole };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount(entry = `/invitation#token=${token}`) {
  const router = createMemoryRouter([{ path: '/invitation', element: <InvitationLinkPage /> }, { path: '/elsewhere', element: <h1>Elsewhere</h1> }], { initialEntries: [entry] });
  return { ...render(<RouterProvider router={router} />), router };
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
describe('Invitation link review and acknowledgment', () => {
  it('scrubs the URL before requests, retains proof only in memory and requires separate review and acceptance', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetch);
    const { router } = mount();
    await waitFor(() => expect(router.state.location.hash).toBe(''));
    expect(fetch).not.toHaveBeenCalled(); expect(document.body.textContent).not.toContain(token);
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: item.organizationName });
    expect(fetch.mock.calls[0][0]).toBe('/invitations/review');
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ token });
    expect(screen.queryByText(/acceptance acknowledged/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Accept reviewed invitation' }));
    expect(await screen.findByRole('link', { name: 'Open Owner Portal' })).toHaveAttribute('href', `/portal/${item.organizationId}`);
    expect(fetch.mock.calls[1][0]).toBe(`/me/invitations/${item.id}/accept`);
    expect(fetch.mock.calls[1][1].body).toBe('{}');
    expect(sessionStorage.length).toBe(0); expect(localStorage.length).toBe(0);
  });
  it('recovers a lost acceptance by the same ID without submitting the bearer again', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockRejectedValueOnce(new Error('Lost acknowledgment')).mockResolvedValueOnce(reply(ack));
    vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Retry invitation acceptance' }));
    await screen.findByRole('link', { name: 'Open Owner Portal' });
    expect(fetch.mock.calls[2][0]).toBe(fetch.mock.calls[1][0]); expect(fetch.mock.calls[2][1].body).toBe('{}');
  });
  it('keeps the scrubbed proof while signing in and requires fresh review afterward', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({}, 401)).mockResolvedValueOnce(reply({ user: { id: item.id, email: 'invited@example.test' }, sessionExpiresAt: '2035-01-01T00:00:00Z' }))
      .mockResolvedValueOnce(reply(item)); vi.stubGlobal('fetch', fetch);
    const { router } = mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: 'StrataAI2' });
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'invited@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'StrataAI2' })).not.toBeInTheDocument());
    expect(router.state.location.pathname).toBe('/invitation'); expect(fetch).toHaveBeenCalledTimes(2);
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: item.organizationName });
    expect(fetch.mock.calls[2][1].body).toBe(fetch.mock.calls[0][1].body);
  });
  it('hides protected metadata after a lost session and recovers the original ID after fresh sign-in', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockResolvedValueOnce(reply({}, 401))
      .mockResolvedValueOnce(reply({ user: { id: item.id, email: 'invited@example.test' }, sessionExpiresAt: '2035-01-01T00:00:00Z' }))
      .mockResolvedValueOnce(reply(ack)); vi.stubGlobal('fetch', fetch); mount();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByRole('heading', { name: 'StrataAI2' });
    expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'invited@example.test' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled());
    expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry invitation acceptance' }));
    await screen.findByRole('link', { name: 'Open Owner Portal' });
    expect(fetch.mock.calls[3][0]).toBe(fetch.mock.calls[1][0]);
  });
  it.each([400, 403, 404, 409])('clears protected preview and prevents writes after admission status %s', async status => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(item)).mockResolvedValueOnce(reply({}, status)); vi.stubGlobal('fetch', fetch); mount();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' })); fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByText(/This link is unavailable/);
    expect(screen.queryByRole('heading', { name: item.organizationName })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Accept|Retry/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  });
  it.each([`/invitation?token=${token}`, `/invitation#token=${token}&token=${token}`, '/invitation#token=short'])('rejects malformed/query proof and scrubs %s', async entry => {
    const fetch = vi.fn(); vi.stubGlobal('fetch', fetch); const { router } = mount(entry);
    await waitFor(() => expect(router.state.location.hash + router.state.location.search).toBe(''));
    expect(screen.queryByRole('button', { name: 'Review invitation' })).not.toBeInTheDocument(); expect(fetch).not.toHaveBeenCalled();
  });
  it('does not accept malformed preview or acknowledge mismatched acceptance', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({ ...item, surface: 'GLOBAL' })).mockResolvedValueOnce(reply(item))
      .mockResolvedValueOnce(reply({ ...ack, invitationId: item.organizationId })); vi.stubGlobal('fetch', fetch); mount();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' })); await screen.findByText(/could not be reviewed/);
    expect(screen.queryByRole('button', { name: 'Accept reviewed invitation' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review invitation' })); fireEvent.click(await screen.findByRole('button', { name: 'Accept reviewed invitation' }));
    await screen.findByRole('button', { name: 'Retry invitation acceptance' });
    expect(screen.queryByRole('link', { name: 'Open Owner Portal' })).not.toBeInTheDocument();
  });
  it('fences duplicate requests and times out a transport that ignores cancellation', async () => {
    const fetch = vi.fn().mockImplementation(() => new Promise(() => {})); vi.stubGlobal('fetch', fetch); mount();
    vi.useFakeTimers(); const button = screen.getByRole('button', { name: 'Review invitation' });
    fireEvent.click(button); fireEvent.click(button); expect(fetch).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(16_000); });
    expect(screen.getByText(/could not be reviewed/)).toBeInTheDocument(); expect(button).toBeEnabled();
  });
  it('ignores late review results after navigation', async () => {
    let resolve!: (value: Response) => void;
    const fetch = vi.fn().mockImplementation(() => new Promise<Response>(done => { resolve = done; })); vi.stubGlobal('fetch', fetch);
    const { router } = mount(); fireEvent.click(screen.getByRole('button', { name: 'Review invitation' }));
    await act(async () => { await router.navigate('/elsewhere'); resolve(reply(item)); });
    expect(screen.getByRole('heading', { name: 'Elsewhere' })).toBeInTheDocument(); expect(screen.queryByText(item.organizationName)).not.toBeInTheDocument();
  });
});
