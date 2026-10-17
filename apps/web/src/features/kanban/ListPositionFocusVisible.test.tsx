import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { ListPositionControls } from './ListPositionControls';

it('keeps the approved MUI focus indicator after an owned admission refresh retains DOM focus', async () => {
  const list = { id: 'moving', name: 'Planning', rank: '500000000000000000000000000000', version: 1, lifecycleState: 'active' };
  const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
    access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [{ list, cards: [] }] };
  const acknowledged = { ...list, version: 2, organizationId: 'org', boardId: 'board' };
  const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify(acknowledged)));
  vi.stubGlobal('fetch', fetcher);
  const refresh = vi.fn(), busy = vi.fn();
  const draw = (disabled: boolean, current = list) => <><ListPositionControls list={current} snapshot={snapshot} disabled={disabled}
    onRefresh={refresh} onBusyChange={busy} /><button>Outside navigation</button></>;
  try {
    const view = render(draw(false));
    fireEvent.click(screen.getByRole('button', { name: 'Move Planning list', exact: true }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm list move', exact: true }));
    await screen.findByText('List move acknowledged. Current ordering is being checked.');
    view.rerender(draw(false, acknowledged));
    const action = screen.getByRole('button', { name: 'Move Planning list', exact: true });
    await waitFor(() => expect(action).toHaveFocus());
    view.rerender(draw(true, acknowledged));
    // Native browsers may retain activeElement while MUI clears focusVisible
    // for disabled admission. Refocusing that same node emits no new focus event.
    expect(action).toHaveFocus();
    view.rerender(draw(false, acknowledged));
    await waitFor(() => expect(action).toHaveClass('Mui-focusVisible'));
    expect(action).toHaveFocus(); expect(fetcher).toHaveBeenCalledOnce();
    const outside = screen.getByRole('button', { name: 'Outside navigation' });
    act(() => outside.focus()); view.rerender(draw(true, acknowledged)); view.rerender(draw(false, acknowledged));
    expect(outside).toHaveFocus(); expect(action).not.toHaveClass('Mui-focusVisible');
  } finally { vi.unstubAllGlobals(); }
});
