import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthPage } from './AuthPage';
import { signInReturnPath } from './signInReturnPath';

const organization = '11111111-1111-4111-8111-111111111111';
const board = '22222222-2222-4222-8222-222222222222';
const card = '33333333-3333-4333-8333-333333333333';
const boardPath = `/app/${organization}/boards/${board}`;
const cardPath = `${boardPath}/cards/${card}`;
function loginResult(email = 'person@example.test') {
  return new Response(JSON.stringify({ user: { id: organization, email }, sessionExpiresAt: new Date(Date.now() + 60_000).toISOString() }), { status: 200 });
}
function show(returnTo: unknown, onAuthenticated?: () => void) {
  return render(<MemoryRouter initialEntries={[{ pathname: '/login', state: { returnTo } }]}>
    <Routes>
      <Route path="/login" element={<AuthPage onAuthenticated={onAuthenticated} />} />
      <Route path="/app" element={<p>Organization chooser</p>} />
      <Route path={boardPath} element={<p>Original Board</p>} />
      <Route path={cardPath} element={<p>Original Card</p>} />
    </Routes>
  </MemoryRouter>);
}
function submit() {
  fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'person@example.test' } });
  fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'correct-private-password' } });
  fireEvent.click(screen.getByRole('button', { name: /^Sign in$/ }));
}
afterEach(() => vi.unstubAllGlobals());

it('PRD-01-VS-04/AC-01: retains existing hash-derived creation IDs without imposing new UUID semantics', () => {
  const path = '/app/17963572-758d-de05-f656-8f1be2da2c0d/boards/17963572-758d-de05-f656-8f1be2da2c0d/cards/17963572-758d-de05-f656-8f1be2da2c0d';
  expect(signInReturnPath(path)).toBe(path);
});

it('PRD-01-VS-04/AC-03: rejects a path with an otherwise matching prefix and trailing line break', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(loginResult())); show(cardPath+'\n'); submit();
  await screen.findByText('Organization chooser');
});

it.each([[boardPath, 'Original Board'], [cardPath, 'Original Card']])('PRD-01-VS-04/AC-01: resumes requested context %s after valid sign-in', async (path, destination) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(loginResult())); show(path); submit();
  await screen.findByText(destination);
});

it.each([undefined, null, 42, { path: cardPath }, 'https://outside.example/app', '//outside.example/app', '/portal/'+organization, '/app/'+organization+'/delete', cardPath+'?token=private', cardPath+'#private', cardPath+'/../delete', cardPath.replace('/app/', '/%61pp/')])('PRD-01-VS-04/AC-03: untrusted return state %j falls back to Organization chooser', async returnTo => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(loginResult())); show(returnTo); submit();
  await screen.findByText('Organization chooser');
});

it('PRD-01-VS-04/AC-02: uncertain sign-in keeps destination and original retry identity until acknowledgment', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('lost response')).mockResolvedValueOnce(loginResult());
  vi.stubGlobal('fetch', fetch); show(cardPath); submit();
  await screen.findByText(/Retry with the same details/);
  expect(screen.queryByText('Original Card')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: /^Sign in$/ }));
  await screen.findByText('Original Card');
  const first = fetch.mock.calls[0][1] as RequestInit, second = fetch.mock.calls[1][1] as RequestInit;
  expect(second.body).toBe(first.body);
  expect(new Headers(second.headers).get('Idempotency-Key')).toBe(new Headers(first.headers).get('Idempotency-Key'));
});

it('PRD-01-VS-04/AC-02: an invalid account acknowledgment cannot open the destination', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(loginResult('other@example.test'))); show(cardPath); submit();
  await screen.findByText(/Retry with the same details/);
  expect(screen.queryByText('Original Card')).not.toBeInTheDocument();
});

it('PRD-01-VS-04/AC-03: invitation-specific authentication callback retains ownership of its workflow', async () => {
  const onAuthenticated = vi.fn(); vi.stubGlobal('fetch', vi.fn().mockResolvedValue(loginResult()));
  show(cardPath, onAuthenticated); submit();
  await waitFor(() => expect(onAuthenticated).toHaveBeenCalledOnce());
  expect(screen.queryByText('Original Card')).not.toBeInTheDocument();
});
