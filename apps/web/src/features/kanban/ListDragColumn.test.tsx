import { fireEvent, render, screen } from '@testing-library/react';
import { ListDragColumn } from './ListDragColumn';
vi.mock('@dnd-kit/core', () => ({
  useDraggable: () => ({ setNodeRef: vi.fn(), setActivatorNodeRef: vi.fn(), attributes: {}, listeners: {} }),
  useDroppable: () => ({ setNodeRef: vi.fn() }),
}));
it('restores independent List section scroll after actual unmount/remount', () => {
  const memory = new Map<string, number>();
  const tree = (id: string) => <ListDragColumn id={id} name={id} available={false} disabled={false} scrollMemory={memory}>
    <h3 id={'list-name-' + id}>{id}</h3><div data-testid="card-viewport">Cards</div>
  </ListDragColumn>;
  const first = render(tree('planning'));
  fireEvent.scroll(screen.getByRole('region', { name: 'planning' }), { target: { scrollTop: 147 } });
  fireEvent.scroll(screen.getByTestId('card-viewport'), { target: { scrollTop: 800 } });
  expect(memory.get('section:planning')).toBe(147);
  first.unmount(); const second = render(tree('other'));
  expect(screen.getByRole('region', { name: 'other' }).scrollTop).toBe(0);
  fireEvent.scroll(screen.getByRole('region', { name: 'other' }), { target: { scrollTop: 29 } });
  second.unmount(); render(tree('planning'));
  expect(screen.getByRole('region', { name: 'planning' }).scrollTop).toBe(147);
  expect(memory.get('section:other')).toBe(29);
});
