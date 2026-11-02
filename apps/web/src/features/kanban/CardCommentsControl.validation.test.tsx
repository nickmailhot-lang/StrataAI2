import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CardCommentsControl } from './CardCommentsControl';
import { workRequest } from '../../api/workManagement';
import { watchIdentity } from '../auth/identityLive';

vi.mock('../../api/workManagement', async original => ({ ...await original<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const comment = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, authorId: profile.id,
  content: 'Existing comment', createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z',
  version: 1, editedAt: null, deletedAt: null, deletedBy: null };
const page = { ...scope, cardVersion: 4, items: [comment], nextCursor: null, canComment: true };
const message = 'Enter valid comment text, up to 10000 characters.';
const writes = () => vi.mocked(workRequest).mock.calls.filter(([, init]) => Boolean(init?.method));
const context = () => ({ ...scope, version: 4, editable: true, disabled: false, unavailable: false,
  onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() });
beforeEach(() => {
  vi.mocked(workRequest).mockReset().mockImplementation(async path => path === '/me' ? profile : page);
  vi.mocked(watchIdentity).mockReset().mockImplementation(() => vi.fn());
});
async function open(edit: boolean) {
  render(<CardCommentsControl {...context()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Review Card comments' }));
  await screen.findByRole('button', { name: 'Add comment' });
  fireEvent.click(screen.getByRole('button', { name: edit ? 'Edit comment' : 'Add comment' }));
  return screen.getByRole('textbox', { name: edit ? 'Edit your comment' : 'New comment' });
}
for (const edit of [false, true]) {
  for (const [kind, text] of [['empty', ' \n\t '], ['control', 'Hello\u0001'], ['unpaired Unicode', 'Hello\ud800']] as const) {
    it(`associates ${kind} validation with the ${edit ? 'edit' : 'new'} comment field without sending a command`, async () => {
      const field = await open(edit);
      fireEvent.change(field, { target: { value: text } });
      fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
      expect(field).toHaveFocus();
      expect(field).toHaveAttribute('aria-invalid', 'true');
      expect(field).toHaveAccessibleDescription(message);
      expect(screen.getByRole('alert')).toHaveTextContent(message);
      expect(field).toHaveValue(text);
      expect(writes()).toHaveLength(0);
      expect(screen.queryByRole('button', { name: 'Retry original comment change' })).not.toBeInTheDocument();
    });
  }
  it(`clears only local validation on deliberate ${edit ? 'edit' : 'new'} comment input and preserves the normalized explicit save`, async () => {
    const field = await open(edit);
    fireEvent.change(field, { target: { value: ' \n\t ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
    expect(field).toHaveAccessibleDescription(message);
    fireEvent.change(field, { target: { value: '  Repaired comment\r\nSecond line  ' } });
    expect(field).not.toHaveAttribute('aria-invalid', 'true');
    expect(field).not.toHaveAccessibleDescription(message);
    expect(screen.queryByText(message)).not.toBeInTheDocument();
    expect(writes()).toHaveLength(0);
    vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? profile : init?.method
      ? { ...scope, cardVersion: 5, changed: true, comment: { ...comment, content: 'Repaired comment\nSecond line',
        ...(edit ? { version: 2, updatedAt: '2026-10-03T09:00:00Z', editedAt: '2026-10-03T09:00:00Z' } : {}) } } : page);
    fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
    await waitFor(() => expect(writes()).toHaveLength(1));
    const [path, init] = writes()[0];
    expect(path).toBe(`/cards/${scope.cardId}/comments${edit ? '/' + comment.id : ''}`);
    expect(init?.method).toBe(edit ? 'PATCH' : 'POST');
    expect(JSON.parse(String(init?.body))).toEqual({ content: 'Repaired comment\nSecond line', cardVersion: 4, ...(edit ? { version: 1 } : {}) });
    expect(init?.headers).toEqual(expect.objectContaining({ 'Idempotency-Key': expect.any(String) }));
    await screen.findByText(edit ? 'Comment saved.' : 'Comment added.');
  });
}
it('keeps a valid explicit comment command operational without a local validation error', async () => {
  const field = await open(false);
  fireEvent.change(field, { target: { value: comment.content } });
  vi.mocked(workRequest).mockImplementation(async (path, init) => path === '/me' ? profile : init?.method
    ? { ...scope, cardVersion: 5, changed: true, comment } : page);
  fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
  await screen.findByText('Comment added.');
  expect(writes()).toHaveLength(1);
  expect(JSON.parse(String(writes()[0][1]?.body))).toEqual({ content: comment.content, cardVersion: 4 });
});
