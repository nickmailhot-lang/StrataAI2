import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createTheme, ThemeProvider } from '@mui/material';
import { RetentionPolicyEditor, type PolicyEditorCommand } from './RetentionPolicyEditor';
import { type RetentionPolicy } from './retentionPolicyClient';
import { restoreRetentionPolicyChange } from './retentionPolicyRecovery';

const organizationId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const policyId = '33333333-3333-4333-8333-333333333333';
const source: RetentionPolicy = { id: policyId, organizationId, version: 3, recordClass: 'governance', category: 'decisions', jurisdiction: null,
  anchor: 'approved', retentionDays: null, action: 'preserve', source: 'Original reviewed source', notes: null,
  createdAt: '2026-10-10T12:00:00.0000001Z', updatedAt: '2026-10-10T12:00:00.0000002Z' };
const theme = createTheme();
const command: PolicyEditorCommand = { sequence: 1, policy: source };
function editor(props: Partial<React.ComponentProps<typeof RetentionPolicyEditor>> = {}) {
  return <ThemeProvider theme={theme}><RetentionPolicyEditor organizationId={organizationId} actorId={actorId} admitted denied={false}
    command={command} completed={vi.fn()} accessLost={vi.fn()} {...props} /></ThemeProvider>;
}
function mockTransport(patch: (options: RequestInit, url: string) => Response | Promise<Response>, readCurrent: () => Response = () => new Response(JSON.stringify(source))) {
  const fetch = vi.fn(async (url: string, options: RequestInit = {}) => options.method === 'PATCH' ? patch(options, url)
    : url === `/organizations/${organizationId}/retention-policies/${policyId}` ? readCurrent()
      : new Response(JSON.stringify(url === '/me'
        ? { id: actorId, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
        : { organizationId, items: [], nextAfter: null })));
  vi.stubGlobal('fetch', fetch); return fetch;
}
function receipt(options: RequestInit) {
  const body = JSON.parse(options.body as string) as { version: number; policy: object };
  return new Response(JSON.stringify({ ...source, ...body.policy, version: body.version + 1 }));
}
async function reviewAndConfirm() {
  fireEvent.click(screen.getByRole('button', { name: 'Review policy change' }));
  await screen.findByRole('dialog', { name: 'Review policy change' });
  expect(screen.getByRole('button', { name: 'Confirm policy change' })).toBeDisabled();
  fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the source and applicability.' }));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm policy change' }));
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); });
describe('PRD-70 MUI policy review and recovery', () => {
  it('requires explicit source review and sends an acknowledged edit at the reviewed revision', async () => {
    const completed = vi.fn(); const fetch = mockTransport(receipt);
    render(editor({ completed })); await screen.findByRole('dialog', { name: 'Edit retention policy' });
    expect(screen.getByRole('combobox', { name: 'Record class' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByLabelText('Category')).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Reviewed source'), { target: { value: 'Explicit replacement source' } });
    await reviewAndConfirm(); await waitFor(() => expect(completed).toHaveBeenCalledTimes(1));
    const patch = fetch.mock.calls.find(([, options]) => options?.method === 'PATCH')!;
    expect(patch[0]).toBe(`/organizations/${organizationId}/retention-policies/${policyId}`);
    expect(JSON.parse(patch[1]!.body as string)).toMatchObject({ version: 3, policy: { source: 'Explicit replacement source', recordClass: 'governance' } });
    expect(restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)).toBeUndefined();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
  it('preserves unsent user work while owning admission is rechecked and requires review again after editing', async () => {
    const view = render(editor()); await screen.findByRole('dialog');
    fireEvent.change(screen.getByLabelText('Reviewed source'), { target: { value: 'Private unsent source' } });
    view.rerender(editor({ admitted: false })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByText(/Your policy work is retained/)).toBeVisible();
    view.rerender(editor()); expect(await screen.findByLabelText('Reviewed source')).toHaveValue('Private unsent source');
    fireEvent.click(screen.getByRole('button', { name: 'Review policy change' }));
    fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Back to draft' }));
    fireEvent.change(screen.getByLabelText('Reviewed source'), { target: { value: 'A revised draft' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review policy change' }));
    expect(screen.getByRole('button', { name: 'Confirm policy change' })).toBeDisabled();
    expect(screen.getByText('Source: A revised draft')).toBeVisible();
  });
  it('recovers an uncertain original across navigation with the original bytes and key', async () => {
    let attempts = 0; const bodies: string[] = []; const keys: string[] = [];
    mockTransport(options => {
      bodies.push(options.body as string); keys.push(new Headers(options.headers).get('Idempotency-Key')!);
      if (++attempts === 1) throw new TypeError('Lost acknowledgment'); return receipt(options);
    });
    const view = render(editor()); await screen.findByRole('dialog'); await reviewAndConfirm();
    await screen.findByText(/The acknowledgment could not be confirmed/);
    expect(restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)).toBeDefined(); view.unmount();
    const completed = vi.fn(); render(editor({ command: null, completed }));
    await screen.findByRole('dialog', { name: 'Recover original policy submission' });
    expect(screen.getByText('Source: Original reviewed source')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Retry original submission' }));
    await waitFor(() => expect(completed).toHaveBeenCalledTimes(1));
    expect(bodies).toHaveLength(2); expect(bodies[1]).toBe(bodies[0]); expect(keys[1]).toBe(keys[0]);
  });
  it('reads current state after a stale refusal, preserves the draft and requires a separately reviewed new revision', async () => {
    const patches: RequestInit[] = []; let reads = 0; const completed = vi.fn();
    mockTransport(options => {
      patches.push(options); return patches.length === 1 ? new Response(JSON.stringify({ code: 'version_conflict', detail: 'Never display private diagnostic' }), { status: 409 }) : receipt(options);
    }, () => ++reads === 1 ? new Response('{}', { status: 503 }) : new Response(JSON.stringify({ ...source, version: 4, source: 'Someone else reviewed this source' })));
    const view = render(editor({ completed })); await screen.findByRole('dialog');
    fireEvent.change(screen.getByLabelText('Reviewed source'), { target: { value: 'My preserved draft' } });
    await reviewAndConfirm(); await screen.findByText(/This submission was refused/);
    const original = restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)!;
    fireEvent.click(screen.getByRole('button', { name: 'Review current policy' }));
    await screen.findByText(/Current policy and recovery could not be confirmed/);
    expect(screen.queryByRole('button', { name: 'Retry original submission' })).not.toBeInTheDocument();
    expect(restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)!.key).toBe(original.key);
    fireEvent.click(screen.getByRole('button', { name: 'Review current policy' }));
    expect(await screen.findByLabelText('Reviewed source')).toHaveValue('My preserved draft');
    expect(screen.getByText('Source: Someone else reviewed this source')).toBeVisible();
    expect(restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)).toBeUndefined();
    view.rerender(editor({ completed, admitted: false }));
    expect(screen.queryByText(/Current revision 4 loaded/)).not.toBeInTheDocument();
    expect(screen.queryByText('Source: Someone else reviewed this source')).not.toBeInTheDocument();
    view.rerender(editor({ completed }));
    expect(await screen.findByLabelText('Reviewed source')).toHaveValue('My preserved draft');
    await reviewAndConfirm(); await waitFor(() => expect(completed).toHaveBeenCalledTimes(1));
    expect(JSON.parse(patches[1].body as string)).toMatchObject({ version: 4, policy: { source: 'My preserved draft' } });
    expect(new Headers(patches[1].headers).get('Idempotency-Key')).not.toBe(original.key);
    expect(screen.queryByText(/Never display private diagnostic/)).not.toBeInTheDocument();
  });
  it('withdraws private work and fences a late mutation response after access is revoked', async () => {
    let finish: (value: Response) => void = () => {}; let sent: RequestInit | undefined;
    mockTransport(options => { sent = options; return new Promise<Response>(resolve => { finish = resolve; }); });
    const completed = vi.fn(); const view = render(editor({ completed })); await screen.findByRole('dialog'); await reviewAndConfirm();
    await waitFor(() => expect(sent).toBeDefined());
    expect(restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)).toBeDefined();
    view.rerender(editor({ completed, admitted: false, denied: true }));
    expect(screen.queryByText(/Original reviewed source/)).not.toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)).toBeUndefined();
    await act(async () => { finish(receipt(sent!)); }); expect(completed).not.toHaveBeenCalled();
  });
  it('validates explicit creation input and focuses an actionable error before any submission', async () => {
    const fetch = mockTransport(receipt); render(editor({ command: { sequence: 1, policy: null } }));
    await screen.findByRole('dialog', { name: 'Create retention policy' });
    fireEvent.click(screen.getByRole('button', { name: 'Review policy change' }));
    await screen.findByText('Choose a record class.');
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Record class' })).toHaveFocus());
    expect(screen.getByText(/Provide a reviewed source/)).toBeVisible(); expect(fetch).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Discard draft' })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
  it('blocks corrupt recovery and another target while preserving an existing draft', async () => {
    const view = render(editor()); await screen.findByRole('dialog');
    fireEvent.change(screen.getByLabelText('Reviewed source'), { target: { value: 'Unsent source to retain' } });
    fireEvent.click(screen.getByRole('button', { name: 'Close and retain work' }));
    view.rerender(editor({ command: { sequence: 2, policy: null } }));
    expect(await screen.findByLabelText('Reviewed source')).toHaveValue('Unsent source to retain');
    view.unmount(); sessionStorage.setItem(`strataai:retention-policy-change:v1:${actorId}:${organizationId}`, 'corrupt private body');
    render(editor({ command: { sequence: 3, policy: null } }));
    await screen.findByRole('button', { name: 'Retry policy recovery' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(screen.queryByText(/corrupt private body/)).not.toBeInTheDocument();
    expect(sessionStorage.getItem(`strataai:retention-policy-change:v1:${actorId}:${organizationId}`)).toBe('corrupt private body');
  });
  it('keeps the reviewed input available when submission storage is unwritable and sends nothing', async () => {
    const fetch = mockTransport(receipt); render(editor()); await screen.findByRole('dialog');
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {});
    await reviewAndConfirm(); await screen.findByText(/Browser recovery could not be confirmed/);
    expect(fetch).not.toHaveBeenCalled();
    expect(within(screen.getByRole('dialog')).getByText('Source: Original reviewed source')).toBeVisible();
  });
  it('creates an explicitly chosen policy with a stable new identity and revision zero', async () => {
    const completed = vi.fn(); const fetch = mockTransport((options, url) => {
      const body = JSON.parse(options.body as string) as { version: number; policy: object };
      return new Response(JSON.stringify({ ...source, ...body.policy, id: url.split('/').at(-1), version: body.version + 1 }));
    });
    render(editor({ command: { sequence: 1, policy: null }, completed }));
    await screen.findByRole('dialog', { name: 'Create retention policy' });
    function choose(name: string, option: string) {
      fireEvent.mouseDown(screen.getByRole('combobox', { name })); fireEvent.click(screen.getByRole('option', { name: option }));
    }
    choose('Record class', 'Operational work'); choose('Period starts at', 'closure'); choose('Disposition action', 'Delete when eligible');
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'work_notes' } });
    fireEvent.change(screen.getByLabelText('Jurisdiction (blank = Organization-wide)'), { target: { value: 'Explicit applicability' } });
    fireEvent.change(screen.getByLabelText('Reviewed source'), { target: { value: 'A reviewed explicit source' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review policy change' }));
    await screen.findByText('Enter 0–3,652,500 whole days. Blank days require Preserve.');
    expect(fetch).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Elapsed UTC days (blank = indefinite preservation)'), { target: { value: '90' } });
    await reviewAndConfirm(); await waitFor(() => expect(completed).toHaveBeenCalledTimes(1));
    const patch = fetch.mock.calls.find(([, options]) => options?.method === 'PATCH')!;
    expect(patch[0].split('/').at(-1)).toMatch(/^[a-f0-9-]{36}$/); expect(patch[0]).not.toContain(policyId);
    expect(JSON.parse(patch[1]!.body as string)).toMatchObject({ version: 0, policy: { recordClass: 'operationalWork', category: 'work_notes',
      jurisdiction: 'Explicit applicability', anchor: 'closed', retentionDays: 90, action: 'delete', source: 'A reviewed explicit source' } });
  });
});
