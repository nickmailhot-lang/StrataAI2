import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle } from '@mui/material';
import { CommentMentionPicker } from './CommentMentionPicker';
import { workRequest, WorkRequestError } from '../../api/workManagement';

vi.mock('../../api/workManagement', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn(),
}));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const actor = id(8);
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const option = (n = 1) => ({ userId: id(100 + n), handle: `member_${String(n).padStart(2, '0')}`, displayName: 'Teammate', handleVersion: 2 });
const page = (items = [option()], nextCursor: string | null = null) => ({ ...scope, cardVersion: 4, prefix: '', items, nextCursor });
const props = () => ({ ...scope, actor, version: 4, disabled: false, onSelect: vi.fn(), onBusyChange: vi.fn() });
function host(value: unknown = page(), refuse = false) {
  let finish: () => void = () => {};
  const held = new Promise<void>(resolve => { finish = resolve; });
  let reads = 0;
  vi.mocked(workRequest).mockImplementation(async path => {
    if (++reads === 1) { await held; if (refuse) throw new WorkRequestError(503, null); }
    return path === '/me' ? profile : value;
  });
  return finish;
}
function View({ context = props() }: { context?: ReturnType<typeof props> }) {
  return <Dialog open><DialogTitle>Teammate lookup</DialogTitle><DialogContent>
    <CommentMentionPicker {...context} />
  </DialogContent><DialogActions><Button>Close</Button></DialogActions></Dialog>;
}
async function begin() {
  const find = screen.getByRole('button', { name: 'Find teammates' });
  act(() => find.focus()); fireEvent.click(find);
  await waitFor(() => expect(find).toBeDisabled());
  return find;
}
beforeEach(() => { vi.mocked(workRequest).mockReset(); });
afterEach(() => { vi.restoreAllMocks(); });

it('parks keyboard focus in the current dialog before disabling Find and restores it after the admitted read', async () => {
  const finish = host(); const context = props(); render(<View context={context} />);
  const find = await begin(); expect(screen.getByRole('dialog')).toHaveFocus();
  await act(async () => finish());
  await waitFor(() => expect(find).toHaveFocus());
  expect(screen.getByRole('button', { name: 'Mention Teammate (@member_01)' })).toBeEnabled();
  expect(vi.mocked(workRequest).mock.calls.map(([path]) => path)).toEqual(['/me', `/cards/${scope.cardId}/mention-options?prefix=`, '/me']);
  expect(context.onSelect).not.toHaveBeenCalled();
});

it('preserves an explicit Close focus during a protected lookup', async () => {
  const finish = host(); render(<View />); await begin();
  const close = screen.getByRole('button', { name: 'Close' }); act(() => close.focus());
  await act(async () => finish()); await screen.findByRole('button', { name: 'Mention Teammate (@member_01)' });
  expect(close).toHaveFocus();
});

it('does not claim focus for a programmatic activation that did not own keyboard focus', async () => {
  const finish = host(); render(<View />);
  const close = screen.getByRole('button', { name: 'Close' }); act(() => close.focus());
  fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  await act(async () => finish()); await screen.findByRole('button', { name: 'Mention Teammate (@member_01)' });
  expect(close).toHaveFocus();
});

it('does not restore a retired read owner after the Card version changes', async () => {
  const finish = host(); const context = props(); const ui = render(<View context={context} />);
  const find = await begin(); ui.rerender(<View context={{ ...context, version: 5 }} />);
  await act(async () => finish());
  await screen.findByText('Unable to review current teammates. Refresh the Card before selecting a mention.');
  expect(screen.queryByRole('button', { name: 'Mention Teammate (@member_01)' })).not.toBeInTheDocument();
  expect(find).not.toHaveFocus(); expect(context.onSelect).not.toHaveBeenCalled();
});

it('does not restore keyboard focus into a hidden document', async () => {
  const finish = host(); render(<View />); const find = await begin();
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
  await act(async () => finish()); await screen.findByRole('button', { name: 'Mention Teammate (@member_01)' });
  expect(find).not.toHaveFocus();
});

it.each([{ actor: id(9) }, { disabled: true }])('does not reclaim a lookup after protected context changes %j', async patch => {
  const finish = host(); const context = props(); const ui = render(<View context={context} />);
  const find = await begin(); ui.rerender(<View context={{ ...context, ...patch }} />);
  await act(async () => finish());
  await screen.findByText('Unable to review current teammates. Refresh the Card before selecting a mention.');
  expect(find).not.toHaveFocus(); expect(context.onSelect).not.toHaveBeenCalled();
  expect(screen.queryByRole('button', { name: 'Mention Teammate (@member_01)' })).not.toBeInTheDocument();
});

it('does not restore into a removed picker when a late protected read completes', async () => {
  const finish = host(); const context = props(); const ui = render(<View context={context} />); await begin();
  ui.unmount();
  const destination = document.createElement('button'); destination.textContent = 'New destination'; document.body.append(destination);
  try {
    destination.focus(); await act(async () => finish());
    expect(destination).toHaveFocus(); expect(context.onSelect).not.toHaveBeenCalled();
    expect(context.onBusyChange.mock.calls).toEqual([[true], [false]]);
  } finally { destination.remove(); }
});

it('restores the owned Find action after a bounded operational refusal without selecting a teammate', async () => {
  const finish = host(undefined, true); const context = props(); render(<View context={context} />);
  const find = await begin(); expect(screen.getByRole('dialog')).toHaveFocus();
  await act(async () => finish());
  await screen.findByText('Unable to review current teammates. Refresh the Card before selecting a mention.');
  await waitFor(() => expect(find).toHaveFocus());
  expect(workRequest).toHaveBeenCalledTimes(1); expect(context.onSelect).not.toHaveBeenCalled();
});

it('uses the current Find action when the focused Next control retires on the final page', async () => {
  const cursor = `${scope.cardId}/4//member_20`; const first = page(Array.from({ length: 20 }, (_, i) => option(i + 1)), cursor);
  let finish: () => void = () => {}; const held = new Promise<void>(resolve => { finish = resolve; }); let lookups = 0;
  vi.mocked(workRequest).mockImplementation(async path => {
    if (path === '/me') return profile;
    if (++lookups === 1) return first;
    await held; return page([option(21)]);
  });
  render(<View />); await begin();
  const next = await screen.findByRole('button', { name: 'Next teammates' });
  act(() => next.focus()); fireEvent.click(next); expect(screen.getByRole('dialog')).toHaveFocus();
  await act(async () => finish()); await screen.findByRole('button', { name: 'Mention Teammate (@member_21)' });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Find teammates' })).toHaveFocus());
  expect(screen.getByRole('button', { name: 'Next teammates' })).toBeDisabled();
  expect(vi.mocked(workRequest).mock.calls.map(([path]) => path)).toEqual(['/me', `/cards/${scope.cardId}/mention-options?prefix=`, '/me', '/me', `/cards/${scope.cardId}/mention-options?prefix=&after=${encodeURIComponent(cursor)}`, '/me']);
});

it('restores the current Next action when another admitted page remains', async () => {
  const cursor = `${scope.cardId}/4//member_20`; const nextCursor = `${scope.cardId}/4//member_40`;
  let finish: () => void = () => {}; const held = new Promise<void>(resolve => { finish = resolve; }); let lookups = 0;
  vi.mocked(workRequest).mockImplementation(async path => {
    if (path === '/me') return profile;
    if (++lookups === 1) return page(Array.from({ length: 20 }, (_, i) => option(i + 1)), cursor);
    await held; return page(Array.from({ length: 20 }, (_, i) => option(i + 21)), nextCursor);
  });
  render(<View />); await begin(); const previous = await screen.findByRole('button', { name: 'Next teammates' });
  act(() => previous.focus()); fireEvent.click(previous); expect(screen.getByRole('dialog')).toHaveFocus();
  await act(async () => finish()); await screen.findByRole('button', { name: 'Mention Teammate (@member_21)' });
  const current = screen.getByRole('button', { name: 'Next teammates' });
  expect(current).not.toBe(previous); expect(current).toBeEnabled(); await waitFor(() => expect(current).toHaveFocus());
  expect(workRequest).toHaveBeenCalledTimes(6);
});
