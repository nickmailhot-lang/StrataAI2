import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { BoardCopyControl } from './BoardCopyControl';
import type { BoardSnapshot } from '../../api/workManagement';
import { configureActivityTelemetry, flushActivityTelemetry } from './activityTelemetry';
const org = '11111111-1111-1111-1111-111111111111', id = '22222222-2222-2222-2222-222222222222';
const user = '33333333-3333-3333-3333-333333333333', target = '44444444-4444-4444-4444-444444444444';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const snapshot: BoardSnapshot = { board: { id, organizationId: org, name: 'Planning', description: 'Private text', version: 1,
  backgroundType: 'COLOR', backgroundValue: 'blue', lifecycleState: 'active' }, lists: [],
  access: { canView: true, canEdit: true, canMove: true, canAdminister: true } };
const result = { ...snapshot.board, id: target, name: 'Planning copy', visibility: 'PRIVATE',
  createdAt: '2026-10-04T12:00:00.1234567Z', updatedAt: '2026-10-04T12:00:00.1234567Z' };
const copied = { ...snapshot, board: { ...result, version: 2, name: 'Current copied name' } };
const props = { snapshot, disabled: false, onBusyChange: vi.fn(), onRecoveryChange: vi.fn(), onRefresh: vi.fn(), onReturnFocus: vi.fn() };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function mount() { return render(<MemoryRouter><BoardCopyControl {...props} /></MemoryRouter>); }
async function open() {
  fireEvent.click(screen.getByRole('button', { name: 'Copy Board' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create Board copy' })).toBeEnabled());
}
afterEach(() => { configureActivityTelemetry(false); vi.unstubAllGlobals(); vi.clearAllMocks(); });
it.each([true, false])('requires a new owned image reference in the copy acknowledgment (independent=%s)', async independent => {
  const sourceImage = '55555555-5555-5555-5555-555555555555';
  const imageSnapshot = { ...snapshot, board: { ...snapshot.board, backgroundType: 'IMAGE', backgroundValue: sourceImage } };
  const imageResult = { ...result, backgroundType: 'IMAGE', backgroundValue: independent ? '66666666-6666-6666-6666-666666666666' : sourceImage };
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile
    : options?.method === 'POST' ? imageResult : path.endsWith(target) ? { ...copied, board: imageResult } : imageSnapshot,
    options?.method === 'POST' ? 201 : 200)));
  render(<MemoryRouter><BoardCopyControl {...props} snapshot={imageSnapshot} /></MemoryRouter>);
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  if (independent) await screen.findByRole('link', { name: 'Open copied Board' });
  else {
    await screen.findByText('This copy is unconfirmed. Keep the name unchanged and retry the same copy.');
    expect(screen.queryByRole('link', { name: 'Open copied Board' })).not.toBeInTheDocument();
  }
});
it('reviews the bound source before copying and reads current destination before offering navigation', async () => {
  const fetch = vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile
    : options?.method === 'POST' ? result : path.endsWith(target) ? copied : snapshot, options?.method === 'POST' ? 201 : 200));
  vi.stubGlobal('fetch', fetch); mount(); expect(fetch).not.toHaveBeenCalled(); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  const link = await screen.findByRole('link', { name: 'Open copied Board' });
  expect(link).toHaveAttribute('href', `/app/${org}/boards/${target}`);
  expect(screen.getByText('Copied Board: Current copied name')).toBeInTheDocument();
  const command = fetch.mock.calls.find(([, options]) => options?.method === 'POST')!;
  expect(command[0]).toBe('/boards/' + id + '/copy');
  expect(JSON.parse(command[1]!.body as string)).toEqual({ name: 'Planning copy', version: 1 });
  expect(new Headers(command[1]?.headers).get('Content-Type')).toBe('application/json');
  expect(new Headers(command[1]?.headers).get('Idempotency-Key')).toBeTruthy();
  expect(new Headers(command[1]?.headers).get('X-StrataAI-Request')).toBe('1');
  await waitFor(() => expect(link).toHaveFocus());
  fireEvent.click(screen.getByRole('button', { name: 'Done copying Board' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Copy Board' })).toHaveFocus());
});
it('preserves original review, name and key through a lost response and a newer source', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') { if (++writes === 1) throw new Error('Private copy diagnostic'); return response(result,201); }
    return response(path.endsWith(target) ? copied : snapshot);
  });
  vi.stubGlobal('fetch', fetch); const view = mount(); await open();
  fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  const retry = await screen.findByRole('button', { name: 'Retry same Board copy' });
  await waitFor(() => expect(retry).toBeEnabled());
  view.rerender(<MemoryRouter><BoardCopyControl {...props} snapshot={{ ...snapshot, board: { ...snapshot.board, version: 2, name: 'Later source', description: 'Later text' } }} /></MemoryRouter>);
  expect(screen.getByRole('textbox', { name: 'Copied Board name' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Cancel Board copy' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private copy diagnostic')).not.toBeInTheDocument();
  fireEvent.click(retry); await screen.findByRole('link', { name: 'Open copied Board' });
  const commands = fetch.mock.calls.filter(([, options]) => options?.method === 'POST'); expect(commands).toHaveLength(2);
  expect(commands[0][1]?.body).toBe(commands[1][1]?.body);
  for (const command of commands) expect(new Headers(command[1]?.headers).get('Content-Type')).toBe('application/json');
  expect(new Headers(commands[0][1]?.headers).get('Idempotency-Key')).toBe(new Headers(commands[1][1]?.headers).get('Idempotency-Key'));
  expect(props.onRecoveryChange).toHaveBeenCalledWith(true);
  await waitFor(() => expect(props.onRecoveryChange).toHaveBeenLastCalledWith(false));
});
it('retires recovery before resubmitting under a changed account', async () => {
  let changed = false;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response({ ...profile, id: changed ? target : user });
    if (options?.method === 'POST') throw new Error('Lost'); return response(snapshot);
  });
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  const retry = await screen.findByRole('button', { name: 'Retry same Board copy' }); await waitFor(() => expect(retry).toBeEnabled());
  changed = true; fireEvent.click(retry); await screen.findByText('Your account changed. Reopen Board copying.');
  expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1);
  expect(screen.queryByRole('link', { name: 'Open copied Board' })).not.toBeInTheDocument();
});
it('retains a name draft through conflict and creates a new intent only after fresh review', async () => {
  let newer = false;
  const updated = { ...snapshot, board: { ...snapshot.board, name: 'New source', version: 2 } };
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') {
      if (!newer) { newer = true; return response({ code: 'version_conflict' },409); }
      return response({ ...result, name: 'Private draft' },201);
    }
    return response(path.endsWith(target) ? copied : newer ? updated : snapshot);
  });
  vi.stubGlobal('fetch',fetch); const view = mount(); await open();
  fireEvent.change(screen.getByRole('textbox',{name:'Copied Board name'}),{target:{value:'Private draft'}});
  fireEvent.click(screen.getByRole('button',{name:'Create Board copy'}));
  await screen.findByText('This copy could not be applied. Review the current Board before trying again.');
  expect(screen.getByRole('button',{name:'Create Board copy'})).toBeDisabled();
  view.rerender(<MemoryRouter><BoardCopyControl {...props} snapshot={updated} /></MemoryRouter>);
  fireEvent.click(screen.getByRole('button',{name:'Review current Board for copy'}));
  await waitFor(() => expect(screen.getByRole('button',{name:'Create Board copy'})).toBeEnabled());
  expect(screen.getByRole('textbox',{name:'Copied Board name'})).toHaveValue('Private draft');
  fireEvent.click(screen.getByRole('button',{name:'Create Board copy'})); await screen.findByRole('link',{name:'Open copied Board'});
  const commands = fetch.mock.calls.filter(([,options]) => options?.method==='POST');
  expect(JSON.parse(commands[1][1]!.body as string)).toEqual({name:'Private draft',version:2});
  expect(new Headers(commands[0][1]?.headers).get('Idempotency-Key')).not.toBe(new Headers(commands[1][1]?.headers).get('Idempotency-Key'));
});
it('retries only destination disclosure after a confirmed copy has a temporary read failure', async () => {
  let reads = 0;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return response(profile); if (options?.method === 'POST') return response(result,201);
    if (path.endsWith(target)) { if (++reads === 1) throw new Error('Private destination'); return response(copied); }
    return response(snapshot);
  });
  vi.stubGlobal('fetch', fetch); mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  await screen.findByText('Copy acknowledged. Check copied Board access before opening it.');
  expect(screen.queryByRole('button', { name: 'Retry same Board copy' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Check copied Board access' })); await screen.findByRole('link', { name: 'Open copied Board' });
  expect(fetch.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1);
});
it('aborts and ignores a late acknowledgment when source admission is withdrawn', async () => {
  let finish!: (value: Response) => void;
  const fetch = vi.fn(async (path: string, options?: RequestInit) => path === '/me' ? response(profile)
    : options?.method === 'POST' ? new Promise<Response>(resolve => { finish = resolve; }) : response(snapshot));
  vi.stubGlobal('fetch', fetch); const view = mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  await waitFor(() => expect(finish).toBeDefined());
  view.rerender(<MemoryRouter><BoardCopyControl {...props} snapshot={{ ...snapshot, access: { ...snapshot.access, canEdit: false } }} /></MemoryRouter>);
  expect(fetch.mock.calls.find(([, options]) => options?.method === 'POST')![1]?.signal?.aborted).toBe(true);
  await act(async () => finish(response(result,201)));
  expect(screen.queryByRole('link', { name: 'Open copied Board' })).not.toBeInTheDocument();
  expect(props.onRecoveryChange).toHaveBeenLastCalledWith(false);
});
it.each(['actor','scope','clock','revision'])('keeps malformed acknowledgments unconfirmed (%s)', async kind => {
  const invalid = kind === 'actor' ? { ...result, id } : kind === 'scope' ? { ...result, organizationId: user }
    : kind === 'clock' ? { ...result, updatedAt: '2026-10-04T12:00:00.1234568Z' } : { ...result, version: 2 };
  vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => response(path === '/me' ? profile : options?.method === 'POST' ? invalid : snapshot)));
  mount(); await open(); fireEvent.click(screen.getByRole('button', { name: 'Create Board copy' }));
  await screen.findByRole('button', { name: 'Retry same Board copy' });
  expect(screen.queryByRole('link', { name: 'Open copied Board' })).not.toBeInTheDocument();
});
it('transmits only fixed copy observations during lost-response recovery', async () => {
  configureActivityTelemetry(true); let writes = 0; const payloads: string[] = [];
  const fetch = vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me/activity-client-events') { payloads.push(options!.body as string); return new Response(null,{status:204}); }
    if (path === '/me') return response(profile);
    if (options?.method === 'POST') { if (++writes === 1) throw new Error('Secret diagnostic'); return response(result,201); }
    return response(path.endsWith(target) ? copied : snapshot);
  });
  vi.stubGlobal('fetch',fetch); mount(); await open(); fireEvent.click(screen.getByRole('button',{name:'Create Board copy'}));
  const retry = await screen.findByRole('button',{name:'Retry same Board copy'}); await waitFor(() => expect(retry).toBeEnabled());
  fireEvent.click(retry); await screen.findByRole('link',{name:'Open copied Board'}); await act(async () => { await flushActivityTelemetry(); await flushActivityTelemetry(); });
  const events = payloads.flatMap(value => JSON.parse(value).events as Record<string,unknown>[]);
  expect(events.some(event => event.action==='board_copy_change' && event.kind==='retry')).toBe(true);
  for (const event of events) expect(Object.keys(event).every(key => ['action','kind','count','durationMs'].includes(key))).toBe(true);
  for (const value of [org,id,user,target,'Planning','Private text','Secret diagnostic']) expect(payloads.join('')).not.toContain(value);
});

it('preserves a chosen destination during the copy dialog exit transition', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => response(path === '/me' ? profile : snapshot)));
  render(<MemoryRouter><a href="/another">Chosen destination</a><BoardCopyControl {...props} /></MemoryRouter>);
  const destination = screen.getByRole('link', { name: 'Chosen destination' });
  await open();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel Board copy' }));
  destination.focus();
  expect(destination).toHaveFocus();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(destination).toHaveFocus();
  expect(props.onReturnFocus).not.toHaveBeenCalled();
});
