import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MentionHandleDialog } from './MentionHandleDialog';

const subject = '11111111-1111-1111-1111-111111111111';
const initial = { userId: subject, handle: 'alice', userVersion: 1, handleVersion: 1,
  createdAt: '2026-10-03T10:00:00Z', updatedAt: '2026-10-03T10:00:00Z' };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const validation = 'Use 3–40 letters, digits or underscores, beginning with a letter. Reserved names cannot be chosen.';
function host(refuse = false) {
  let setting = { ...initial };
  return vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return json({ id: subject, version: setting.userVersion, status: 'ACTIVE' });
    if (options?.method !== 'PATCH') return json(setting);
    if (refuse) return json({ code: 'version_conflict' }, 409);
    const body = JSON.parse(String(options.body)) as { handle: string };
    setting = { ...setting, handle: body.handle, userVersion: 2, handleVersion: 2, updatedAt: '2026-10-03T10:00:01Z' };
    return json({ userId: subject, handle: body.handle, userVersion: 2, handleVersion: 2, changed: true });
  });
}
afterEach(() => vi.unstubAllGlobals());

it.each(['', '_invalid', 'board'])('identifies and describes invalid handle %j without issuing a command', async value => {
  const fetchMock = host(); vi.stubGlobal('fetch', fetchMock);
  render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
  const field = screen.getByRole('textbox', { name: 'Mention handle' });
  await waitFor(() => expect(field).toBeEnabled());
  const reads = fetchMock.mock.calls.length;
  fireEvent.change(field, { target: { value } });
  field.focus();
  fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
  expect(field).toHaveAttribute('aria-invalid', 'true');
  expect(field).toHaveAccessibleDescription(validation);
  expect(screen.getByRole('alert')).toHaveTextContent(validation);
  expect(field).toHaveValue(value);
  expect(field).toHaveFocus();
  expect(fetchMock).toHaveBeenCalledTimes(reads);
  expect(screen.getByRole('button', { name: 'Save handle' })).toBeEnabled();
});

it('clears field validation on editing and confirms a deliberately submitted valid handle', async () => {
  const fetchMock = host(); vi.stubGlobal('fetch', fetchMock); const close = vi.fn();
  render(<MentionHandleDialog open subject={subject} onClose={close} onDenied={vi.fn()} />);
  const field = screen.getByRole('textbox', { name: 'Mention handle' });
  await waitFor(() => expect(field).toBeEnabled());
  fireEvent.change(field, { target: { value: 'board' } });
  fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
  expect(field).toHaveAttribute('aria-invalid', 'true');
  fireEvent.change(field, { target: { value: 'bob' } });
  expect(field).not.toHaveAttribute('aria-invalid', 'true');
  expect(field).toHaveAccessibleDescription('3–40 letters, digits or underscores. Start with a letter. Names are case insensitive.');
  expect(fetchMock.mock.calls.filter(([, options]) => options?.method === 'PATCH')).toHaveLength(0);
  fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
  await screen.findByText('Handle change confirmed.');
  const writes = fetchMock.mock.calls.filter(([, options]) => options?.method === 'PATCH');
  expect(writes).toHaveLength(1);
  expect(JSON.parse(String(writes[0][1]?.body))).toEqual({ handle: 'bob', userVersion: 1, handleVersion: 1 });
  expect(new Headers(writes[0][1]?.headers).get('X-StrataAI-Expected-User')).toBe(subject);
  expect(field).toHaveValue('bob');
  fireEvent.click(screen.getByRole('button', { name: 'Close' })); expect(close).toHaveBeenCalledOnce();
});

it('keeps an account conflict as an operational refusal rather than invalid field input', async () => {
  vi.stubGlobal('fetch', host(true));
  render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
  const field = screen.getByRole('textbox', { name: 'Mention handle' });
  await waitFor(() => expect(field).toBeEnabled());
  fireEvent.change(field, { target: { value: 'bob' } });
  fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
  await screen.findByText(/Your account changed elsewhere/);
  expect(field).not.toHaveAttribute('aria-invalid', 'true');
  expect(field).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Save handle' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Review current setting' })).toBeEnabled();
});
