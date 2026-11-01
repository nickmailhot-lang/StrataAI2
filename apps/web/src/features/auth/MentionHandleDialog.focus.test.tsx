import { act, fireEvent, render, screen } from '@testing-library/react';
import { MentionHandleDialog } from './MentionHandleDialog';
const subject = '11111111-1111-1111-1111-111111111111';
const setting = { userId: subject, handle: 'alice', userVersion: 1, handleVersion: 1,
  createdAt: '2026-10-03T10:00:00Z', updatedAt: '2026-10-03T10:00:00Z' };
const json = (value: unknown) => new Response(JSON.stringify(value));
function host(held = true) {
  let finish: () => void = () => {};
  const pending = new Promise<Response>(resolve => { finish = () => resolve(json(setting)); });
  const fetchMock = vi.fn(async (path: string) => path === '/me'
    ? json({ id: subject, version: 1, status: 'ACTIVE' }) : held ? pending : json(setting));
  vi.stubGlobal('fetch', fetchMock); return { finish, fetchMock };
}
beforeEach(() => vi.useFakeTimers());
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); vi.useRealTimers(); });
const open = () => render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
const entered = () => act(() => vi.advanceTimersByTimeAsync(300));
it('focuses the admitted field when the protected read finishes after dialog entry', async () => {
  const { finish, fetchMock } = host(); open(); await entered();
  const field = screen.getByRole('textbox', { name: 'Mention handle' });
  expect(field).toBeDisabled(); expect(screen.getByRole('dialog')).toHaveFocus();
  await act(async () => finish());
  expect(field).toBeEnabled(); expect(field).toHaveValue('alice'); expect(field).toHaveFocus();
  expect(fetchMock.mock.calls.map(([path]) => path)).toEqual(['/me', '/me/mention-handle', '/me']);
});
it('does not reclaim initial focus after the user subsequently chooses Close', async () => {
  const { finish } = host(); const view = open(); await entered();
  await act(async () => finish());
  expect(screen.getByRole('textbox', { name: 'Mention handle' })).toHaveFocus();
  const close = screen.getByRole('button', { name: 'Close' }); expect(close).toBeEnabled(); act(() => close.focus());
  view.rerender(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
  await act(async () => {}); expect(close).toHaveFocus();
});
it('does not reclaim Close focus at transition completion when the field was already admitted', async () => {
  host(false); open(); await act(async () => {});
  const field = screen.getByRole('textbox', { name: 'Mention handle' }); expect(field).toBeEnabled();
  const close = screen.getByRole('button', { name: 'Close' }); expect(close).toBeEnabled(); act(() => close.focus());
  await entered(); expect(close).toHaveFocus();
});
it('does not move focus into the field while the document is hidden or later reclaim it', async () => {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
  const { finish } = host(); open(); await entered();
  await act(async () => finish());
  const field = screen.getByRole('textbox', { name: 'Mention handle' }); expect(field).toBeEnabled(); expect(field).not.toHaveFocus();
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
  fireEvent(document, new Event('visibilitychange')); await act(async () => {}); expect(field).not.toHaveFocus();
});
it('aborts a closed dialog read without focusing or sending a follow-up request on its late completion', async () => {
  const { finish, fetchMock } = host(); const view = open(); await entered();
  const close = vi.fn(); view.rerender(<MentionHandleDialog open={false} subject={subject} onClose={close} onDenied={vi.fn()} />);
  await act(async () => finish()); await entered();
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(fetchMock).toHaveBeenCalledTimes(2);
});
