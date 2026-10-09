import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationSettingsPage } from './OrganizationSettingsPage';
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { organizationId: string; userId: string;
  invalidate(): void; reset(): void; unavailable(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const profile = { id: '22222222-2222-4222-8222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
let currentProfile = profile;
function stubFetch(delegate: (path: string, options?: RequestInit) => unknown) {
  vi.stubGlobal('fetch', (path: string, options?: RequestInit) => path === '/me'
    ? Promise.resolve(reply(currentProfile)) : delegate(path, options));
}
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
beforeEach(() => { currentProfile = profile; vi.clearAllMocks(); });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('PRD-03-TC-01/05/06/08 Organization metadata administration', () => {
  it.each([403, 404])('clears private draft on a definite pre-save profile refusal (%s)', async status => {
    let checks = 0; let writes = 0;
    vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
      if (path === '/me') return ++checks === 3 ? reply({}, status) : reply(profile);
      if (init.method === 'PATCH') writes++;
      return reply(summary);
    }));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    await screen.findByText('Organization settings are unavailable to your account.');
    expect(writes).toBe(0); expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
  });
  it('does not invent an original save when the pre-save account check fails', async () => {
    let checks = 0; let writes = 0;
    vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
      if (path === '/me') return ++checks === 3 ? reply({}, 503) : reply(profile);
      if (init.method === 'PATCH') { writes++; return reply({ ...org, version: 2 }); }
      return reply(summary);
    }));
    mount(); fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Preserved draft' } });
    fireEvent.click(save()); await screen.findByText(/No save was sent/);
    expect(writes).toBe(0); expect(save()).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Preserved draft');
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' }));
    await screen.findByText('Name: Council');
    fireEvent.click(screen.getByRole('button', { name: 'Keep draft after review' })); expect(save()).toBeEnabled();
  });
  it('preserves a submitted edit and exact retry key when its final account check is unavailable', async () => {
    let checks = 0; const writes: RequestInit[] = [];
    vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => {
      if (path === '/me') return ++checks === 4 ? reply({}, 503) : reply(profile);
      if (init.method === 'PATCH') { writes.push(init); return reply({ ...org, version: 2 }); }
      return reply(summary);
    }));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original save' }));
    await screen.findByText(/Original save acknowledgment recovered/);
    expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
    expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
  });
  it('bounds the entire save including both profile checks and fences late JSON', async () => {
    let checks = 0; let writes = 0; let signal!: AbortSignal; let finishBody!: (value: unknown) => void;
    vi.stubGlobal('fetch', vi.fn((path: string, init: RequestInit = {}) => {
      if (path === '/me') {
        checks++;
        if (checks === 3) return new Promise<Response>(resolve => setTimeout(() => resolve(reply(profile)), 8_000));
        if (checks === 4) {
          signal = init.signal!; const stalled = reply(profile);
          stalled.json = () => new Promise<unknown>(resolve => { finishBody = resolve; }); return Promise.resolve(stalled);
        }
        return Promise.resolve(reply(profile));
      }
      if (init.method === 'PATCH') { writes++; return Promise.resolve(reply({ ...org, version: 2 })); }
      return Promise.resolve(reply(summary));
    }));
    mount(); await screen.findByLabelText(/^Organization name/); vi.useFakeTimers();
    await act(async () => fireEvent.click(save()));
    await act(async () => vi.advanceTimersByTimeAsync(8_000)); expect(writes).toBe(1); expect(finishBody).toBeDefined();
    await act(async () => vi.advanceTimersByTimeAsync(7_001)); expect(signal.aborted).toBe(true);
    expect(screen.getByRole('button', { name: 'Retry original save' })).toBeEnabled();
    await act(async () => finishBody(profile));
    expect(screen.queryByText('Organization settings saved.')).not.toBeInTheDocument(); expect(writes).toBe(1);
  });
  it('withdraws stale review controls and requires a fresh read after a failed refresh', async () => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary))
      .mockResolvedValueOnce(reply({ ...summary, organization: { ...org, name: 'Other admin', version: 2 } }))
      .mockResolvedValueOnce(reply({}, 503)));
    mount(); fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Draft' } });
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Other admin');
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText(/Unable to load current settings/);
    expect(screen.queryByRole('button', { name: 'Keep draft after review' })).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Draft'); expect(save()).toBeDisabled();
  });
  it('reads only the scoped Organization and refuses a directory-shaped response', async () => {
    const fetcher = vi.fn().mockResolvedValue(reply([summary])); stubFetch(fetcher); mount();
    await screen.findByText(/Unable to load current settings/);
    expect(fetcher).toHaveBeenCalledWith('/organizations/org-1', expect.anything());
    expect(screen.queryByDisplayValue('Council')).not.toBeInTheDocument();
  });
  it('preserves typing and focus during live refresh and requires explicit review of a newer saved version', async () => {
    let reads = 0; let finish!: (response: Response) => void;
    stubFetch(vi.fn(async () => ++reads === 1 ? reply(summary) : new Promise<Response>(resolve => { finish = resolve; })));
    mount(); const input = await screen.findByLabelText(/^Organization name/); input.focus();
    fireEvent.change(input, { target: { value: 'Unsaved draft' } });
    act(() => live.watch.mock.calls[0][0].invalidate());
    await waitFor(() => expect(finish).toBeDefined()); expect(input).toBeEnabled(); expect(input).toHaveFocus();
    fireEvent.change(input, { target: { value: 'Typed during refresh' } });
    await act(async () => finish(reply({ ...summary, organization: { ...org, name: 'Other admin', version: 2 } })));
    await screen.findByText('Name: Other admin'); expect(input).toHaveValue('Typed during refresh'); expect(input).toHaveFocus();
    expect(save()).toBeDisabled(); fireEvent.click(screen.getByRole('button', { name: 'Keep draft after review' }));
    expect(input).toHaveValue('Typed during refresh'); expect(save()).toBeEnabled();
  });
  it.each(['invalidate', 'reset', 'unavailable'] as const)('retains the original save key/body and warning while %s reads expose a later saved version', async recovery => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost acknowledgment'))
      .mockResolvedValueOnce(reply({ ...summary, organization: { ...org, name: 'Later saved name', version: 3 } }))
      .mockResolvedValueOnce(reply({ ...org, name: 'Original edit', version: 2 }));
    stubFetch(fetcher); mount(); fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Original edit' } });
    fireEvent.click(save()); await screen.findByText(/Your save could not be confirmed/);
    act(() => live.watch.mock.calls[0][0][recovery]()); await screen.findByText('Name: Later saved name');
    expect(screen.getByText(/Your save could not be confirmed/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Original edit');
    expect(screen.getByRole('button', { name: 'Keep draft after review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Discard draft and use current settings' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original save' }));
    await screen.findByText(/Original save acknowledgment recovered/);
    expect(fetcher.mock.calls[3][1].body).toBe(fetcher.mock.calls[1][1].body);
    expect(fetcher.mock.calls[3][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[1][1].headers.get('Idempotency-Key'));
  });
  it('clears an unresolved save and its private draft when live recovery withdraws administration', async () => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
      .mockResolvedValueOnce(reply({ ...summary, role: 2 })));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    await screen.findByRole('button', { name: 'Retry original save' });
    act(() => live.watch.mock.calls[0][0].unavailable()); await screen.findByText('Organization settings are unavailable to your account.');
    expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
  });
  it('rejects account replacement during the initial read before exposing Organization fields', async () => {
    stubFetch(vi.fn(async () => { currentProfile = { ...profile, id: '33333333-3333-4333-8333-333333333333' }; return reply(summary); }));
    mount(); await screen.findByText('Sign in destination'); expect(screen.queryByDisplayValue('Council')).not.toBeInTheDocument();
  });
  it('refuses a draft save after account replacement before submitting a PATCH', async () => {
    const fetcher = vi.fn().mockResolvedValue(reply(summary)); stubFetch(fetcher); mount();
    await screen.findByLabelText(/^Organization name/); currentProfile = { ...profile, id: '33333333-3333-4333-8333-333333333333' };
    fireEvent.click(save()); await screen.findByText('Sign in destination'); expect(fetcher).toHaveBeenCalledTimes(1);
  });
  it('saves validated metadata with the current version and reflects the authoritative result', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({ ...org, name: 'Updated', version: 2 }));
    stubFetch(fetcher); mount();
    fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Updated' } });
    fireEvent.click(save()); await screen.findByText('Organization settings saved.');
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ name: 'Updated', description: org.description, logoUrl: null, version: 1 });
    expect(fetcher.mock.calls[1][1].headers.get('X-StrataAI-Request')).toBe('1');
    expect(fetcher.mock.calls[0][1].headers.get('X-StrataAI-Expected-Actor')).toBe(profile.id);
    expect(fetcher.mock.calls[1][1].headers.get('X-StrataAI-Expected-Actor')).toBe(profile.id);
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Updated');
  });
  it('preserves a conflicting draft until current metadata is reviewed explicitly', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({ code: 'version_conflict' }, 409))
      .mockResolvedValueOnce(reply({ ...summary, organization: { ...org, name: 'Other admin', version: 2 } }))
      .mockResolvedValueOnce(reply({ ...org, name: 'My draft', version: 3 }));
    stubFetch(fetcher); mount();
    fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'My draft' } }); fireEvent.click(save());
    await screen.findByText(/The Organization changed elsewhere/); expect(save()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Other admin');
    expect(screen.getByLabelText(/^Organization name/)).toHaveValue('My draft');
    fireEvent.click(screen.getByRole('button', { name: 'Keep draft after review' })); fireEvent.click(save());
    await screen.findByText('Organization settings saved.'); expect(JSON.parse(fetcher.mock.calls[3][1].body).version).toBe(2);
  });
  it('recovers a lost acknowledgment with the original key and then reviews later authoritative metadata', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('lost response'))
      .mockResolvedValueOnce(reply({ ...org, name: 'My draft', version: 2 }))
      .mockResolvedValueOnce(reply({ ...summary, organization: { ...org, name: 'Later edit', version: 3 } }));
    stubFetch(fetcher); mount();
    fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'My draft' } }); fireEvent.click(save());
    await screen.findByText(/Your save could not be confirmed/); expect(save()).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Load current settings' })).toBeDisabled();
    expect(screen.getByLabelText(/^Organization name/)).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original save' }));
    await screen.findByText(/Original save acknowledgment recovered/);
    expect(fetcher.mock.calls[2][1].body).toBe(fetcher.mock.calls[1][1].body);
    expect(fetcher.mock.calls[2][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[1][1].headers.get('Idempotency-Key'));
    expect(fetcher.mock.calls[2][1].headers.get('X-StrataAI-Expected-Actor')).toBe(profile.id);
    expect(fetcher.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(save()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Later edit');
    expect(fetcher.mock.calls.filter(call => call[1]?.method === 'PATCH')).toHaveLength(2);
    expect(screen.queryByText('Organization settings saved.')).not.toBeInTheDocument();
  });
  it.each([401, 403, 404])('clears the preserved retry and private draft when recovery loses access (%s)', async status => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
      .mockResolvedValueOnce(reply({}, status)));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    fireEvent.click(await screen.findByRole('button', { name: 'Retry original save' }));
    await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization settings are unavailable to your account.');
    expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue(org.description)).not.toBeInTheDocument();
  });
  it('keeps the same command after repeated uncertainty and releases it only after an expired refusal', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
      .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({ code: 'idempotency_expired' }, 409));
    stubFetch(fetcher); mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
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
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({}, status)));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    await screen.findByText(status === 401 ? 'Sign in destination' : 'Organization settings are unavailable to your account.');
    expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue(org.description)).not.toBeInTheDocument();
  });
  it.each([{ role: 2, status: 0 }, { role: 0, status: 1 }, { role: 0, status: 2 }])('does not expose administration to a member or inactive Organization (%j)', async ({ role, status }) => {
    const fetcher = vi.fn().mockResolvedValueOnce(reply({ ...summary, role, organization: { ...org, status } })); stubFetch(fetcher); mount();
    await screen.findByText('Organization settings are unavailable to your account.'); expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
  });
  it('rejects a malformed current record without exposing cached metadata', async () => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply({ ...summary, organization: { ...org, version: '1' } })));
    mount(); await screen.findByText(/Unable to load current settings/); expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
  });
  it('clears private draft and reviewed metadata when a refresh loses access', async () => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({}, 403)));
    mount(); await screen.findByLabelText(/^Organization name/);
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' }));
    await screen.findByText('Organization settings are unavailable to your account.');
    expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
    expect(screen.queryByText('Current saved settings')).not.toBeInTheDocument();
  });
  it('rejects a wrong-Organization acknowledgment and preserves the original draft for review', async () => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({ ...org, id: 'other', version: 2 })));
    mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
    await screen.findByText(/Your save could not be confirmed/); expect(save()).toBeDisabled(); expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Council');
  });
  it('bounds an abort-ignoring save and fences its late result after navigation', async () => {
    let complete: ((value: Response) => void) | undefined;
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockImplementationOnce(() => new Promise<Response>(resolve => { complete = resolve; })));
    const router = mount(); await screen.findByLabelText(/^Organization name/); vi.useFakeTimers();
    fireEvent.click(save()); fireEvent.click(save());
    await act(async () => { for (let step = 0; step < 10 && !complete; step++) await Promise.resolve(); });
    expect(complete).toBeDefined();
    await act(async () => { vi.advanceTimersByTime(15_000); });
    expect(screen.getByText(/Your save could not be confirmed/)).toBeVisible(); expect(save()).toBeDisabled();
    await act(async () => { await router.navigate('/elsewhere'); complete?.(reply({ ...org, version: 2 })); });
    expect(router.state.location.pathname).toBe('/elsewhere'); expect(screen.queryByText('Organization settings saved.')).not.toBeInTheDocument();
  });
  it('allows discarding a stale draft only after the current settings load', async () => {
    stubFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({ ...summary, organization: { ...org, name: 'Current', version: 2 } })));
    mount(); fireEvent.change(await screen.findByLabelText(/^Organization name/), { target: { value: 'Unsaved' } });
    fireEvent.click(screen.getByRole('button', { name: 'Load current settings' })); await screen.findByText('Name: Current');
    fireEvent.click(screen.getByRole('button', { name: 'Discard draft and use current settings' }));
    await waitFor(() => expect(screen.getByLabelText(/^Organization name/)).toHaveValue('Current')); expect(save()).toBeEnabled();
  });
});


it('shows the admitted read-failure support reference without diagnostic body text', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile)
    : new Response(JSON.stringify({ title: 'private diagnostic', detail: 'private body' }), { status: 503, headers: { 'X-Correlation-ID': 'read.reference-1' } })));
  mount(); await screen.findByText('Reference: read.reference-1');
  expect(screen.getByText('Unable to load current settings. Your draft is preserved. Please retry.')).toBeVisible();
  expect(screen.queryByText(/private diagnostic|private body/)).not.toBeInTheDocument();
});
it.each([400, 503])('pairs a settings save refusal with its own safe reference (%s)', async status => {
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => path === '/me' ? reply(profile)
    : init.method === 'PATCH' ? new Response(JSON.stringify({ title: 'private diagnostic', detail: 'private body' }), { status, headers: { 'X-Correlation-ID': 'save.reference-1' } }) : reply(summary)));
  mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
  await screen.findByText('Reference: save.reference-1');
  expect(screen.queryByText(/private diagnostic|private body/)).not.toBeInTheDocument();
  if (status === 503) expect(screen.getByRole('button', { name: 'Retry original save' })).toBeVisible();
  else expect(screen.queryByRole('button', { name: 'Retry original save' })).not.toBeInTheDocument();
});
it('does not expose a prior save reference after final account replacement', async () => {
  let checks = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => path === '/me' ? reply(++checks === 4 ? { ...profile, id: '33333333-3333-4333-8333-333333333333' } : profile)
    : init.method === 'PATCH' ? new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'retired-save-reference' } }) : reply(summary)));
  mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
  await screen.findByRole('heading', { name: 'Sign in destination' });
  expect(screen.queryByText(/retired-save-reference/)).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/^Organization name/)).not.toBeInTheDocument();
});
it('keeps malformed response metadata out of the settings error display', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => path === '/me' ? reply(profile)
    : new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'private:diagnostic' } })));
  mount(); await screen.findByText('Unable to load current settings. Your draft is preserved. Please retry.');
  expect(screen.queryByText(/private:diagnostic|Reference:/)).not.toBeInTheDocument();
});

it('preserves the original uncertain-save reference through a protected background refresh', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => path === '/me' ? reply(profile)
    : init.method === 'PATCH' ? new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'original-save-reference' } }) : reply(summary)));
  mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
  await screen.findByText('Reference: original-save-reference');
  act(() => live.watch.mock.calls.at(-1)![0].invalidate());
  await screen.findByText('Current settings checked. Review any saved changes before replacing them with your draft.');
  expect(screen.getByText('Reference: original-save-reference')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Retry original save' })).toBeVisible();
});
it('reports the actual final actor-check failure instead of an unadmitted save response reference', async () => {
  let checks = 0;
  vi.stubGlobal('fetch', vi.fn(async (path: string, init: RequestInit = {}) => path === '/me'
    ? ++checks === 4 ? new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'final-actor-check-reference' } }) : reply(profile)
    : init.method === 'PATCH' ? new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'unadmitted-save-reference' } }) : reply(summary)));
  mount(); await screen.findByLabelText(/^Organization name/); fireEvent.click(save());
  await screen.findByText('Reference: final-actor-check-reference');
  expect(screen.queryByText(/unadmitted-save-reference/)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Retry original save' })).toBeVisible();
});
