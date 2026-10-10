import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OrganizationConfigurationActivityPanel } from './OrganizationConfigurationActivityPanel';
import { WorkRequestError } from '../../api/workManagement';
const reader = vi.hoisted(() => vi.fn());
vi.mock('./organizationConfigurationClient', () => ({ readOrganizationConfigurationActivity: reader }));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const item = (version = 2) => ({ eventId: `33333333-3333-4333-8333-${String(version).padStart(12, '0')}`, actorId: actor,
  version, correlationId: 'Source', createdAt: '2026-10-10T13:00:00.0000001Z' });
afterEach(() => reader.mockReset());
describe('PRD-27 private configuration Activity controls', () => {
  it('pages immutable source references and restores heading focus after an initiated read', async () => {
    reader.mockResolvedValueOnce({ items: [item()], nextCursor: 'opaque_cursor' }).mockResolvedValueOnce({ items: [item(1)], nextCursor: null });
    render(<OrganizationConfigurationActivityPanel organizationId={org} actorId={actor} disabled={false} onDenied={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Load configuration Activity' }));
    await screen.findByText('Configuration changed at revision 2');
    expect(screen.getByRole('heading', { name: 'Configuration Activity' })).toHaveFocus();
    expect(screen.getByText(item().createdAt)).toHaveAttribute('datetime', item().createdAt);
    fireEvent.click(screen.getByRole('button', { name: 'Older configuration Activity' }));
    await screen.findByText('Configuration changed at revision 1');
    expect(screen.queryByText('Configuration changed at revision 2')).not.toBeInTheDocument();
    expect(reader.mock.calls[1][3]).toBe('opaque_cursor');
  });
  it('clears unconfirmed rows on failure and offers retry without any mutation', async () => {
    reader.mockResolvedValueOnce({ items: [item()], nextCursor: 'opaque_cursor' }).mockRejectedValueOnce(new WorkRequestError(503, null))
      .mockResolvedValueOnce({ items: [], nextCursor: null });
    render(<OrganizationConfigurationActivityPanel organizationId={org} actorId={actor} disabled={false} onDenied={vi.fn()} />);
    fireEvent.click(screen.getByText('Load configuration Activity')); await screen.findByText('Configuration changed at revision 2');
    fireEvent.click(screen.getByText('Older configuration Activity')); await screen.findByRole('button', { name: 'Retry Activity read' });
    expect(screen.queryByText('Configuration changed at revision 2')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Retry Activity read')); await screen.findByText('No configuration changes exist in this Activity page.');
    expect(reader.mock.calls[2][3]).toBe('opaque_cursor');
  });
  it('withdraws private parent context on denial and cancels a read after unmount', async () => {
    const denied = vi.fn(); reader.mockRejectedValueOnce(new WorkRequestError(404, null));
    const rendered = render(<OrganizationConfigurationActivityPanel organizationId={org} actorId={actor} disabled={false} onDenied={denied} />);
    fireEvent.click(screen.getByText('Load configuration Activity')); await waitFor(() => expect(denied).toHaveBeenCalledOnce());
    reader.mockImplementationOnce(() => new Promise(() => {}));
    fireEvent.click(screen.getByText('Load configuration Activity')); const signal = reader.mock.calls[1][2] as AbortSignal;
    rendered.unmount(); expect(signal.aborted).toBe(true);
  });
  it('does not steal focus from editing elsewhere while a read completes', async () => {
    let finish!: (value: unknown) => void; reader.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    render(<><input aria-label="Unsent configuration draft" /><OrganizationConfigurationActivityPanel organizationId={org} actorId={actor} disabled={false} onDenied={vi.fn()} /></>);
    fireEvent.click(screen.getByText('Load configuration Activity')); const draft = screen.getByLabelText('Unsent configuration draft'); draft.focus();
    await act(async () => finish({ items: [item()], nextCursor: null })); expect(draft).toHaveFocus();
  });
});
