import { fireEvent, render, screen } from '@testing-library/react';
import { CommentMentionPicker } from './CommentMentionPicker';
import { workRequest, WorkRequestError } from '../../api/workManagement';

vi.mock('../../api/workManagement', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/workManagement')>(), workRequest: vi.fn(),
}));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const actor = id(8);
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const validation = 'Use a username prefix, up to 40 letters, digits or underscores.';
function open() {
  const props = { ...scope, actor, version: 4, disabled: false, onSelect: vi.fn(), onBusyChange: vi.fn() };
  render(<CommentMentionPicker {...props} />);
  return props;
}
beforeEach(() => { vi.mocked(workRequest).mockReset(); });

it.each(['@alice', '_alice', 'a'.repeat(41)])('identifies invalid teammate prefix %j without a protected request', value => {
  const props = open();
  const field = screen.getByRole('textbox', { name: 'Teammate username prefix' });
  fireEvent.change(field, { target: { value } });
  fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  expect(field).toHaveAttribute('aria-invalid', 'true');
  expect(field).toHaveAccessibleDescription(validation);
  expect(screen.getByRole('alert')).toHaveTextContent(validation);
  expect(field).toHaveValue(value);
  expect(workRequest).not.toHaveBeenCalled();
  expect(props.onSelect).not.toHaveBeenCalled();
  expect(props.onBusyChange).not.toHaveBeenCalled();
});

it('clears only local validation on edit and looks up a normalized prefix only on deliberate search', async () => {
  const option = { userId: id(9), handle: 'alice', displayName: 'Alice', handleVersion: 2 };
  vi.mocked(workRequest).mockImplementation(async path => path === '/me' ? profile
    : { ...scope, cardVersion: 4, prefix: 'al', items: [option], nextCursor: null });
  const props = open(); const field = screen.getByRole('textbox', { name: 'Teammate username prefix' });
  fireEvent.change(field, { target: { value: '@alice' } });
  fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  expect(field).toHaveAttribute('aria-invalid', 'true');
  fireEvent.change(field, { target: { value: ' AL ' } });
  expect(field).not.toHaveAttribute('aria-invalid', 'true');
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(workRequest).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Mention Alice (@alice)' }));
  expect(workRequest).toHaveBeenCalledTimes(3);
  expect(vi.mocked(workRequest).mock.calls.map(([path]) => path)).toEqual([
    '/me', `/cards/${scope.cardId}/mention-options?prefix=al`, '/me',
  ]);
  expect(props.onSelect).toHaveBeenCalledExactlyOnceWith(option);
});

it('keeps an operational refusal separate from invalid prefix feedback', async () => {
  vi.mocked(workRequest).mockImplementation(async () => { throw new WorkRequestError(403, null); });
  const props = open(); const field = screen.getByRole('textbox', { name: 'Teammate username prefix' });
  fireEvent.change(field, { target: { value: 'alice' } });
  fireEvent.click(screen.getByRole('button', { name: 'Find teammates' }));
  await screen.findByText('Unable to review current teammates. Refresh the Card before selecting a mention.');
  expect(field).not.toHaveAttribute('aria-invalid', 'true');
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(props.onSelect).not.toHaveBeenCalled();
  expect(workRequest).toHaveBeenCalledTimes(1);
});
