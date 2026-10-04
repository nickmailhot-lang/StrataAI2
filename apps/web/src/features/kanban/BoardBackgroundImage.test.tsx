import { fireEvent, render } from '@testing-library/react';
import type { BoardSnapshot } from '../../api/workManagement';
import { BoardBackgroundImage } from './BoardBackgroundImage';
const snapshot: BoardSnapshot = { board: { id: '22222222-2222-2222-2222-222222222222',
  organizationId: '11111111-1111-1111-1111-111111111111', name: 'Board', description: null, version: 1,
  lifecycleState: 'active', backgroundType: 'IMAGE', backgroundValue: '33333333-3333-3333-3333-333333333333' },
  lists: [], access: { canView: true, canEdit: true, canMove: true, canAdminister: true } };
it('uses only the current Board route and withdraws bytes when a fresh snapshot is unavailable', () => {
  const view = render(<BoardBackgroundImage snapshot={snapshot} unavailable={false} />);
  expect(view.container.querySelector('img')).toHaveAttribute('src', `/boards/${snapshot.board.id}/background/image?boardVersion=1`);
  expect(view.container.querySelector('img')).toHaveAttribute('alt', '');
  view.rerender(<BoardBackgroundImage snapshot={snapshot} unavailable />);
  expect(view.container.querySelector('img')).toBeNull();
});
it('falls back after delivery refusal and only retries on a new selected revision', () => {
  const view = render(<BoardBackgroundImage snapshot={snapshot} unavailable={false} />);
  fireEvent.error(view.container.querySelector('img')!);
  view.rerender(<BoardBackgroundImage snapshot={snapshot} unavailable={false} />);
  expect(view.container.querySelector('img')).toBeNull();
  view.rerender(<BoardBackgroundImage snapshot={{ ...snapshot, board: { ...snapshot.board, version: 2 } }} unavailable={false} />);
  expect(view.container.querySelector('img')).toHaveAttribute('src', `/boards/${snapshot.board.id}/background/image?boardVersion=2`);
});
it.each(['https://private.example/image', 'url(secret)', '00000000-0000-0000-0000-000000000000'])('does not render unsupported persisted image values: %s', value => {
  const view = render(<BoardBackgroundImage snapshot={{ ...snapshot, board: { ...snapshot.board, backgroundValue: value } }} unavailable={false} />);
  expect(view.container.querySelector('img')).toBeNull();
});
