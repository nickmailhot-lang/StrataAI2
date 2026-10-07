import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MentionHandleDialog } from './MentionHandleDialog';
const subject = '11111111-1111-1111-1111-111111111111';
const foreign = '22222222-2222-2222-2222-222222222222';
const initial = { userId: subject, handle: 'alice', userVersion: 1, handleVersion: 1,
  createdAt: '2026-10-03T10:00:00Z', updatedAt: '2026-10-03T10:00:00Z' };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function host(mode: 'lost' | 'conflict' | 'malformed' = 'lost') {
  let setting = { ...initial }; let writes = 0;
  return vi.fn(async (path: string, options?: RequestInit) => {
    if (path === '/me') return json({ id: subject, version: setting.userVersion, status: 'ACTIVE' });
    if (options?.method !== 'PATCH') return json(setting);
    writes++;
    if (mode === 'conflict') return json({ code: 'version_conflict', handle: 'private' }, 409);
    const body = JSON.parse(options.body as string) as { handle: string };
    setting = { ...setting, handle: body.handle, userVersion: 2, handleVersion: 2, updatedAt: '2026-10-03T10:00:01Z' };
    if (writes === 1 && mode === 'lost') throw new Error('Reply lost after commit');
    return json({ userId: subject, handle: body.handle, userVersion: 2, handleVersion: mode === 'malformed' ? 3 : 2, changed: true });
  });
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
describe('guarded account handle dialog', () => {
  it.each(['transport', 'body'])('bounds a stalled %s, retries its original intent and ignores the late reply', async phase => {
    vi.useFakeTimers(); let finish: (value: unknown) => void = () => {}; let writes = 0; let version = 1;
    const stalled = new Promise(resolve => { finish = resolve; });
    const acknowledgment = { userId: subject, handle: 'bob', userVersion: 2, handleVersion: 2, changed: true };
    const fetchMock = vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') return json({ id: subject, version, status: 'ACTIVE' });
      if (options?.method !== 'PATCH') return json({ ...initial, userVersion: version, handleVersion: version,
        handle: version === 1 ? 'alice' : 'bob', updatedAt: version === 1 ? initial.updatedAt : '2026-10-03T10:00:01Z' });
      version = 2;
      if (++writes === 1) return phase === 'transport' ? stalled : { status: 200, ok: true, json: () => stalled };
      return json(acknowledgment);
    });
    vi.stubGlobal('fetch', fetchMock);
    render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
    await act(async () => {}); expect(screen.getByLabelText('Mention handle')).toHaveValue('alice');
    fireEvent.change(screen.getByLabelText('Mention handle'), { target: { value: 'bob' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' })); await act(async () => {});
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(screen.getByText(/Unable to confirm your handle change/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original handle change' })); await act(async () => {});
    expect(screen.getByText('Handle change confirmed.')).toBeInTheDocument();
    const attempts = fetchMock.mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(attempts).toHaveLength(2);
    expect(attempts[0][1]!.body).toBe(attempts[1][1]!.body); expect(attempts[0][1]!.signal!.aborted).toBe(true);
    expect(new Headers(attempts[0][1]!.headers).get('Idempotency-Key')).toBe(new Headers(attempts[1][1]!.headers).get('Idempotency-Key'));
    for (const [, options] of attempts) expect(new Headers(options!.headers).get('X-StrataAI-Expected-User')).toBe(initial.userId);
    const calls = fetchMock.mock.calls.length;
    await act(async () => { finish(phase === 'transport' ? json(acknowledgment) : acknowledgment); });
    expect(fetchMock).toHaveBeenCalledTimes(calls); expect(screen.getByLabelText('Mention handle')).toHaveValue('bob');
  });
  it('recovers a lost reply with the identical original key/body and blocks edits and dismissal', async () => {
    const fetchMock = host(); vi.stubGlobal('fetch', fetchMock); const close = vi.fn();
    render(<MentionHandleDialog open subject={subject} onClose={close} onDenied={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Mention handle')).toHaveValue('alice'));
    act(() => screen.getByLabelText('Mention handle').focus());
    fireEvent.change(screen.getByLabelText('Mention handle'), { target: { value: ' BOB ' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
    await screen.findByText(/Unable to confirm your handle change/);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original handle change' })).toHaveFocus());
    expect(screen.getByLabelText('Mention handle')).toBeDisabled(); expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled();
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' }); expect(close).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original handle change' }));
    await screen.findByText('Handle change confirmed.');
    const writes = fetchMock.mock.calls.filter(([, options]) => options?.method === 'PATCH'); expect(writes).toHaveLength(2);
    expect(writes[0][1]!.body).toBe(writes[1][1]!.body);
    expect(new Headers(writes[0][1]!.headers).get('Idempotency-Key')).toBe(new Headers(writes[1][1]!.headers).get('Idempotency-Key'));
    expect(JSON.parse(writes[0][1]!.body as string)).toEqual({ handle: 'bob', userVersion: 1, handleVersion: 1 });
    expect(screen.getByLabelText('Mention handle')).toHaveValue('bob');
    fireEvent.click(screen.getByRole('button', { name: 'Close' })); expect(close).toHaveBeenCalledOnce();
  });
  it('retires a definitive refusal and requires explicit review before a fresh save', async () => {
    const fetchMock = host('conflict'); vi.stubGlobal('fetch', fetchMock);
    render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Mention handle')).toHaveValue('alice'));
    fireEvent.change(screen.getByLabelText('Mention handle'), { target: { value: 'bob' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
    await screen.findByText(/Your account changed elsewhere/); expect(screen.queryByText('private')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save handle' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Review current setting' }));
    await waitFor(() => expect(screen.getByLabelText('Mention handle')).toHaveValue('alice'));
    expect(screen.getByRole('button', { name: 'Save handle' })).toBeEnabled();
  });
  it('retains original intent when a success acknowledgment has the wrong child revision', async () => {
    vi.stubGlobal('fetch', host('malformed'));
    render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
    await waitFor(() => expect(screen.getByLabelText('Mention handle')).toHaveValue('alice'));
    fireEvent.change(screen.getByLabelText('Mention handle'), { target: { value: 'bob' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
    await screen.findByText(/Unable to confirm your handle change/);
    expect(screen.queryByText('Handle change confirmed.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry original handle change' })).toBeEnabled();
  });
  it('denies an account switch during final read without showing protected handle content', async () => {
    const denied = vi.fn(); const fetchMock = vi.fn().mockResolvedValueOnce(json({ id: subject, version: 1, status: 'ACTIVE' }))
      .mockResolvedValueOnce(json(initial)).mockResolvedValueOnce(json({ id: foreign, version: 1, status: 'ACTIVE' }));
    vi.stubGlobal('fetch', fetchMock);
    render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={denied} />);
    await waitFor(() => expect(denied).toHaveBeenCalledOnce()); expect(screen.getByLabelText('Mention handle')).toHaveValue('');
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });
  it('aborts an unmounted read and sends no follow-up request when the transport returns late', async () => {
    let finish: (response: Response) => void = () => {};
    const fetchMock = vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal('fetch', fetchMock);
    const view = render(<MentionHandleDialog open subject={subject} onClose={vi.fn()} onDenied={vi.fn()} />);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce()); view.unmount();
    await act(async () => { finish(json({ id: subject, version: 1, status: 'ACTIVE' })); });
    expect(fetchMock).toHaveBeenCalledOnce();
  });
});
