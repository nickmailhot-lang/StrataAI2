import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { StrataLotDirectoryPage } from './StrataLotDirectoryPage';
import { emptyLot } from './strataLotClient';
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222', lotId = '33333333-3333-4333-8333-333333333333';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const originalData = { ...emptyLot, unitIdentifier: '101', lotIdentifier: 'SL1' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
let lots: { id: string; organizationId: string; version: number; data: typeof originalData; createdAt: string; updatedAt: string }[];
let role: number; let lost: boolean; let writes: RequestInit[];
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/property/units', element: <StrataLotDirectoryPage /> },
    { path: '/app/:organizationId', element: <h1>Organization destination</h1> }], { initialEntries: [`/app/${org}/property/units`] });
  return { ...render(<RouterProvider router={router} />), router };
}
beforeEach(() => {
  lots = []; role = 0; lost = false; writes = []; sessionStorage.clear(); vi.clearAllMocks();
  vi.stubGlobal('fetch', vi.fn(async (path: string, options: RequestInit = {}) => {
    if (path === '/me') return reply(profile);
    if (path === `/organizations/${org}`) return reply({ organization: { id: org, status: 0 }, role });
    if (options.method === 'PATCH') {
      writes.push(options); const body = JSON.parse(String(options.body));
      if (body.version !== (lots[0]?.version ?? 0) && writes.length === 1) return reply({ code: 'version_conflict' }, 409);
      if (!lots[0] || body.version === lots[0].version) lots = [{ id: path.split('/').at(-1)!, organizationId: org, version: body.version + 1,
        data: body.data, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }];
      if (lost && writes.length === 1) throw new TypeError('lost acknowledgment');
      return reply(lots[0]);
    }
    if (path.endsWith('/history')) return reply({ organizationId: org, strataLotId: lots[0].id,
      items: [{ lot: lots[0], actorId: actor, eventId: actor, correlationId: 'safe' }], nextBeforeVersion: null });
    if (path.includes('/strata-lots/')) return reply(lots[0]);
    return reply({ organizationId: org, items: lots, nextCursor: null });
  }));
});
afterEach(() => { vi.unstubAllGlobals(); sessionStorage.clear(); });
async function create() {
  fireEvent.click(await screen.findByRole('button', { name: 'Add unit' }));
  fireEvent.change(screen.getByLabelText(/^Unit identifier/), { target: { value: '101' } });
  fireEvent.change(screen.getByLabelText(/^Lot identifier/), { target: { value: 'SL1' } });
  fireEvent.click(screen.getByRole('button', { name: 'Review unit change' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirm unit change' }));
}
it('PRD-28-TC-01/02: first permitted action creates, refreshes and reviews canonical history', async () => {
  mount(); await screen.findByText(/No units have been recorded/); await create();
  await screen.findByText('Change acknowledged. Current unit revision 1.');
  fireEvent.click(await screen.findByRole('button', { name: 'View unit history' }));
  await screen.findByRole('heading', { name: 'Revision 1' });
  expect(screen.getByRole('region', { name: 'Unit history' })).toHaveTextContent('101');
  fireEvent.click(screen.getByRole('button', { name: 'Close unit editor' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Refresh current records' }));
  await screen.findByRole('button', { name: 'Unit 101 · Lot SL1' }); expect(lots).toHaveLength(1);
});
it('PRD-28-TC-07: uncertain response freezes the draft and recovers the exact original after remount', async () => {
  lost = true; const first = mount(); await create();
  await screen.findByRole('button', { name: 'Retry original unit change' });
  expect(screen.getByLabelText(/^Unit identifier/)).toBeDisabled(); first.unmount();
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Retry original unit change' }));
  await screen.findByText('Change acknowledged. Current unit revision 1.');
  expect(writes).toHaveLength(2); expect(writes[1].body).toBe(writes[0].body);
  expect(new Headers(writes[1].headers).get('Idempotency-Key')).toBe(new Headers(writes[0].headers).get('Idempotency-Key'));
});
it('PRD-28-TC-06: a live authority reset withdraws private drafts after role loss', async () => {
  mount(); await create(); await screen.findByText('Change acknowledged. Current unit revision 1.');
  role = 2;
  const options = live.watch.mock.calls[0][0] as unknown as { reset(): void };
  await act(async () => options.reset());
  await screen.findByText('The unit directory is unavailable to your account.');
  expect(screen.queryByLabelText(/^Unit identifier/)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Retry original unit change' })).not.toBeInTheDocument();
  expect(sessionStorage.length).toBe(0);
});
it('PRD-28-TC-08/16: stale-version refusal requires a fresh read before explicitly retaining a draft', async () => {
  lots = [{ id: lotId, organizationId: org, version: 1, data: originalData, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }];
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Unit 101 · Lot SL1' }));
  fireEvent.change(await screen.findByLabelText(/^Unit identifier/), { target: { value: '102' } });
  lots[0] = { ...lots[0], version: 2, data: { ...originalData, building: 'Other admin' } };
  fireEvent.click(screen.getByRole('button', { name: 'Review unit change' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirm unit change' }));
  await screen.findByText(/This unit changed elsewhere/);
  expect(screen.queryByRole('button', { name: 'Keep draft after review' })).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Refresh current records' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Keep draft after review' }));
  expect(screen.getByLabelText(/^Unit identifier/)).toHaveValue('102');
  fireEvent.click(screen.getByRole('button', { name: 'Review unit change' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirm unit change' }));
  await screen.findByText('Change acknowledged. Current unit revision 3.');
  expect(JSON.parse(String(writes[1].body)).version).toBe(2);
});
it('UXA-04: navigation protects unsent work and returns after explicit discard', async () => {
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Add unit' }));
  fireEvent.change(screen.getByLabelText(/^Unit identifier/), { target: { value: 'Draft' } });
  fireEvent.click(await screen.findByRole('link', { name: 'Return to Organization' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Keep editing' }));
  expect(screen.getByLabelText(/^Unit identifier/)).toHaveValue('Draft');
  fireEvent.click(await screen.findByRole('link', { name: 'Return to Organization' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Leave unit draft' }));
  await screen.findByRole('heading', { name: 'Organization destination' });
});
it.each([1, 2])('PRD-28-TC-04: admits Admin but refuses Member (%s)', async nextRole => {
  role = nextRole; mount();
  if (nextRole === 1) await screen.findByRole('button', { name: 'Add unit' });
  else { await screen.findByText('The unit directory is unavailable to your account.'); expect(screen.queryByRole('button', { name: 'Add unit' })).not.toBeInTheDocument(); }
});
it('PRD-28-TC-07/09: an authority recheck queues behind a submitted command without aborting or duplicating it', async () => {
  const request = fetch; let finish!: () => void; let submittedSignal: AbortSignal | null | undefined;
  vi.stubGlobal('fetch', async (path: string, options: RequestInit = {}) => {
    if (options.method === 'PATCH') { submittedSignal = options.signal; await new Promise<void>(resolve => { finish = resolve; }); }
    return request(path, options);
  });
  mount(); await create(); await waitFor(() => expect(finish).toBeDefined());
  await act(async () => live.watch.mock.calls[0][0].reset());
  expect(submittedSignal?.aborted).toBe(false);
  await act(async () => finish());
  await screen.findByText('Change acknowledged. Current unit revision 1.');
  await screen.findByLabelText(/^Unit identifier/);
  expect(writes).toHaveLength(1); expect(sessionStorage.length).toBe(0);
});
it('LOT-FR-006/UXA-04: successful authority rechecks retain an admitted history page', async () => {
  mount(); await create(); await screen.findByText('Change acknowledged. Current unit revision 1.');
  fireEvent.click(await screen.findByRole('button', { name: 'View unit history' }));
  await screen.findByRole('heading', { name: 'Revision 1' });
  await act(async () => live.watch.mock.calls[0][0].reset());
  await screen.findByRole('heading', { name: 'Revision 1' });
});
it('UXA-06: history focus waits for its section to commit even if a frame runs earlier', async () => {
  lots = [{ id: lotId, organizationId: org, version: 1, data: originalData, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }];
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Unit 101 · Lot SL1' }));
  const launcher = await screen.findByRole('button', { name: 'View unit history' });
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { callback(performance.now()); return 1; });
  launcher.focus(); fireEvent.click(launcher);
  const historyTitle = await screen.findByRole('heading', { name: /^Unit history$/ });
  await waitFor(() => expect(historyTitle).toHaveFocus());
});
it.each([false, true])('UXA-06: a history focus request survives authority retirement without stealing a later choice (%s)', async chooseElsewhere => {
  lots = [{ id: lotId, organizationId: org, version: 1, data: originalData, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }];
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Unit 101 · Lot SL1' }));
  const launcher = await screen.findByRole('button', { name: 'View unit history' });
  const returnLink = screen.getByRole('link', { name: 'Return to Organization' });
  const frames: FrameRequestCallback[] = [];
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { frames.push(callback); return frames.length; });
  launcher.focus(); fireEvent.click(launcher);
  await screen.findByRole('heading', { name: 'Revision 1' });
  const request = fetch; let finish!: () => void;
  vi.stubGlobal('fetch', async (path: string, options: RequestInit = {}) => {
    if (path.endsWith('/strata-lots')) await new Promise<void>(resolve => { finish = resolve; });
    return request(path, options);
  });
  await act(async () => live.watch.mock.calls[0][0].reset());
  await waitFor(() => expect(finish).toBeDefined());
  expect(screen.queryByRole('heading', { name: /^Unit history$/ })).not.toBeInTheDocument();
  await act(async () => frames.splice(0).forEach(callback => callback(performance.now())));
  if (chooseElsewhere) returnLink.focus();
  await act(async () => finish());
  const historyTitle = await screen.findByRole('heading', { name: /^Unit history$/ });
  await waitFor(() => expect(chooseElsewhere ? returnLink : historyTitle).toHaveFocus());
});
it.each([false, true])('UXA-06: restores the retired field owner without stealing a later navigation choice (%s)', async chooseElsewhere => {
  mount(); await create(); await screen.findByText('Change acknowledged. Current unit revision 1.');
  const returnLink = await screen.findByRole('link', { name: 'Return to Organization' });
  const field = await screen.findByLabelText(/^Unit identifier/); field.focus();
  const request = fetch; let finish!: () => void;
  vi.stubGlobal('fetch', async (path: string, options: RequestInit = {}) => {
    if (path.endsWith('/strata-lots')) await new Promise<void>(resolve => { finish = resolve; });
    return request(path, options);
  });
  await act(async () => live.watch.mock.calls[0][0].reset());
  await waitFor(() => expect(finish).toBeDefined());
  if (chooseElsewhere) returnLink.focus();
  await act(async () => finish());
  const restored = await screen.findByLabelText(/^Unit identifier/);
  await waitFor(() => expect(chooseElsewhere ? returnLink : restored).toHaveFocus());
});
it('UXA-07: storage failure prevents dispatch and keeps the unsent draft editable', async () => {
  mount(); await screen.findByRole('button', { name: 'Add unit' });
  const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota'); });
  try {
    await create(); await screen.findByText('This browser could not retain the original submission. No new change was sent.');
    expect(writes).toHaveLength(0); expect(screen.queryByRole('button', { name: 'Retry original unit change' })).not.toBeInTheDocument();
    expect(await screen.findByLabelText(/^Unit identifier/)).toBeEnabled();
  } finally { storage.mockRestore(); }
});
it('PRD-28-TC-03/07: a failed existing-unit opening cannot attach its source to a later new-unit draft', async () => {
  lots = [{ id: lotId, organizationId: org, version: 1, data: originalData, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' }];
  const request = fetch;
  vi.stubGlobal('fetch', async (path: string, options: RequestInit = {}) => path.endsWith(`/${lotId}`) ? reply({}, 503) : request(path, options));
  mount(); fireEvent.click(await screen.findByRole('button', { name: 'Unit 101 · Lot SL1' }));
  await screen.findByRole('alert');
  fireEvent.click(await screen.findByRole('button', { name: 'Add unit' }));
  fireEvent.change(screen.getByLabelText(/^Unit identifier/), { target: { value: 'New draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Refresh current records' }));
  await screen.findByRole('heading', { name: 'Add unit' });
  expect(screen.getByLabelText(/^Unit identifier/)).toHaveValue('New draft');
  expect(screen.getByRole('button', { name: 'View unit history' })).toBeDisabled();
});
it('PRD-28-TC-07: a pending original cannot be presented under another unit after closing its editor', async () => {
  lost = true; mount(); await create();
  await screen.findByRole('button', { name: 'Retry original unit change' });
  const original = lots[0];
  lots.push({ ...original, id: '44444444-4444-4444-8444-444444444444', data: { ...originalData, unitIdentifier: '202', lotIdentifier: 'SL2' } });
  lots.sort((left, right) => left.id.localeCompare(right.id));
  fireEvent.click(await screen.findByRole('button', { name: 'Leave editor; retain original for recovery' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Refresh current records' }));
  const other = await screen.findByRole('button', { name: 'Unit 202 · Lot SL2' });
  fireEvent.click(other);
  expect(screen.queryByRole('region', { name: 'Unit editor' })).not.toBeInTheDocument();
  expect(other).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Recover original unit change' }));
  expect(await screen.findByLabelText(/^Unit identifier/)).toHaveValue('101');
  expect(screen.getByLabelText(/^Lot identifier/)).toHaveValue('SL1');
  expect(screen.getByRole('button', { name: 'Retry original unit change' })).toBeEnabled();
  expect(writes).toHaveLength(1);
});
