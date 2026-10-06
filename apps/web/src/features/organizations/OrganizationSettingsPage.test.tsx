import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationSettingsPage } from './OrganizationSettingsPage';
const org = { id: 'org-1', name: 'Council', description: 'Original description', logoUrl: null, status: 0, version: 1 };
const summary = { organization: org, role: 0 };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
function mount() {
  const router = createMemoryRouter([
    { path: '/app/:organizationId/settings', element: <OrganizationSettingsPage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
    { path: '/elsewhere', element: <h1>Other destination</h1> },
  ], { initialEntries: ['/app/org-1/settings'] });
  render(<RouterProvider router={router} />); return router;
}
const save = () => screen.getByRole('button', { name: 'Save Organization settings' });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('PRD-03-TC-01/05/06/08 Organization metadata administration', () => {
  it('saves validated metadata with the current version and reflects the authoritative result', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply([summary])).mockResolvedValueOnce(reply({ ...org, name: 'Updated', version: 2 }));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Updated' } });
    fireEvent.click(save()); await screen.findByText('Organization settings saved.');
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ name: 'Updated', description: org.description, logoUrl: null, version: 1 });
    expect(fetcher.mock.calls[1][1].headers.get('X-StrataAI-Request')).toBe('1');
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Updated');
  });
  it('preserves a conflicting draft until current metadata is reviewed explicitly', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply([summary])).mockResolvedValueOnce(reply({ code: 'version_conflict' }, 409))
      .mockResolvedValueOnce(reply([{ ...summary, organization: { ...org, name: 'Other admin', version: 2 } }]))
      .mockResolvedValueOnce(reply({ ...org, name: 'My draft', version: 3 }));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'My draft' } }); fireEvent.click(save());
    await screen.findByText(/The Organization changed elsewhere/); expect(save()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Other admin');
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('My draft');
    fireEvent.click(screen.getByRole('button', { name: 'Keep draft after review' })); fireEvent.click(save());
    await screen.findByText('Organization settings saved.'); expect(JSON.parse(fetcher.mock.calls[3][1].body).version).toBe(2);
  });
  it('recovers a lost acknowledgment with the original key and then reviews later authoritative metadata', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply([summary])).mockRejectedValueOnce(new Error('lost response'))
      .mockResolvedValueOnce(reply({ ...org, name: 'My draft', version: 2 }))
      .mockResolvedValueOnce(reply([{ ...summary, organization: { ...org, name: 'Later edit', version: 3 } }]));
    vi.stubGlobal('fetch', fetcher); mount();
    fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'My draft' } }); fireEvent.click(save());
    await screen.findByText(/Your save could not be confirmed/); expect(save()).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Load current settings' })).toBeDisabled();
    expect(screen.getByLabelText(/^Organization name/)).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original save' }));
    await screen.findByText(/Original save acknowledgment recovered/);
    expect(fetcher.mock.calls[2][1].body).toBe(fetcher.mock.calls[1][1].body);
    expect(fetcher.mock.calls[2][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[1][1].headers.get('Idempotency-Key'));
    expect(fetcher.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(save()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Later edit');
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PATCH')).toHaveLength(2);
    expect(screen.queryByText('Organization settings saved.')).not.toBeInTheDocument();
  });
  it.each([401, 403, 404])('clears the preserved retry and private draft when recovery loses access (%s)', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([summary])).mockRejectedValueOnce(new Error('Lost'))
      .mockResolvedValueOnce(reply({}, status)));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original save' }));
    await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization settings are unavailable to your account.');
    expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue(org.description)).not.toBeInTheDocument();
  });
  it('keeps the same command after repeated uncertainty and releases it only after an expired refusal', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply([summary])).mockRejectedValueOnce(new Error('Lost'))
      .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({ code: 'idempotency_expired' }, 409));
    vi.stubGlobal('fetch', fetcher); mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original save' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original save' })).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Load current settings' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original save' }));
    await screen.findByText(/The Organization changed elsewhere/);
    expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
    expect(save()).toBeDisabled(); expect(screen.getByRole('button', { name: 'Load current settings' })).toBeEnabled();
    const writes = fetcher.mock.calls.slice(1);
    expect(new Set(writes.map(call => call[1].body)).size).toBe(1);
    expect(new Set(writes.map(call => call[1].headers.get('Idempotency-Key'))).size).toBe(1);
  });
  it.each([401, 403, 404])('clears private metadata when a save loses access (%s)', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([summary])).mockResolvedValueOnce(reply({}, status)));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization settings are unavailable to your account.');
    expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue(org.description)).not.toBeInTheDocument();
  });
  it.each([{ role: 2, status: 0 }, { role: 0, status: 1 }, { role: 0, status: 2 }])('does not expose administration to a member or inactive Organization (%j)', async ({ role, status }) => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply([{ ...summary, role, organization: { ...org, status } }])); vi.stubGlobal('fetch', fetcher); mount();
    await screen.findByText('Organization settings are unavailable to your account.'); expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
  });
  it('rejects a malformed current record without exposing cached metadata', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([{ ...summary, organization: { ...org, version: '1' } }])));
    mount(); await screen.findByText(/Unable to load current settings/); expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
  });
  it('clears private draft and reviewed metadata when a refresh loses access', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([summary])).mockResolvedValueOnce(reply({}, 403)));
    mount(); await screen.findByLabelText(/^Organization name/);
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' }));
    await screen.findByText('Organization settings are unavailable to your account.');
    expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
    expect(screen.queryByText('Current saved settings')).not.toBeInTheDocument();
  });
  it('rejects a wrong-Organization acknowledgment and preserves the original draft for review', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([summary])).mockResolvedValueOnce(reply({ ...org, id: 'other', version: 2 })));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    await screen.findByText(/Your save could not be confirmed/); expect(save()).toBeDisabled(); expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Council');
  });
  it('bounds an abort-ignoring save and fences its late result after navigation', async () => {
    let complete: ((value: Response) => void) | undefined;
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([summary])).mockImplementationOnce(() => new Promise<Response>(resolve => { complete = resolve; })));
    const router = mount(); await screen.findByLabelText(/^Organization name/); vi.useFakeTimers();
    fireEvent.click(save()); fireEvent.click(save());
    await act(async () => { vi.advanceTimersByTime(15_000); });
    expect(screen.getByText(/Your save could not be confirmed/)).toBeVisible(); expect(save()).toBeDisabled();
    await act(async () => { await router.navigate('/elsewhere'); complete?.(reply({ ...org, version: 2 })); });
    expect(router.state.location.pathname).toBe('/elsewhere'); expect(screen.queryByText('Organization settings saved.')).not.toBeInTheDocument();
  });
  it('allows discarding a stale draft only after the current settings load', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply([summary])).mockResolvedValueOnce(reply([{ ...summary, organization: { ...org, name: 'Current', version: 2 } }])));
    mount(); fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Unsaved' } });
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Current');
    fireEvent.click(screen.getByRole('button', { name: 'Discard draft and use current settings' }));
    await waitFor(() => expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Current')); expect(save()).toBeEnabled();
  });
});
