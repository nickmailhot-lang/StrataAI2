import { render, screen } from '@testing-library/react';
import { CardDragItem } from './CardDragItem';
import { ListDragColumn } from './ListDragColumn';

const registry = vi.hoisted(() => ({ dragNode: vi.fn(), dropNode: vi.fn(), activator: vi.fn(),
  dragDisabled: false, dropDisabled: false }));
vi.mock('@dnd-kit/core', () => ({
  useDraggable: ({ disabled }: { disabled: boolean }) => {
    registry.dragDisabled = disabled;
    return { setNodeRef: registry.dragNode, setActivatorNodeRef: registry.activator, attributes: {}, listeners: {} };
  },
  useDroppable: ({ disabled }: { disabled: boolean }) => {
    registry.dropDisabled = disabled; return { setNodeRef: registry.dropNode };
  },
}));
beforeEach(() => { vi.clearAllMocks(); });

it.each(['Card', 'List'])('keeps the %s drag node registered across a disabled-state render and cleans it up on unmount', kind => {
  const tree = (disabled: boolean) => kind === 'Card'
    ? <CardDragItem id="card" title="Current Card" available disabled={disabled}>Card content</CardDragItem>
    : <ListDragColumn id="list" name="Current List" available disabled={disabled}><h3 id="list-name-list">Current List</h3></ListDragColumn>;
  const view = render(tree(false));
  const original = registry.dragNode.mock.calls[0][0];
  expect(original).toBeInstanceOf(HTMLElement);
  expect(registry.dragNode).toHaveBeenCalledTimes(1); expect(registry.dropNode).toHaveBeenCalledTimes(1);
  view.rerender(tree(true));
  expect(screen.getByRole('button', { name: kind === 'Card' ? 'Drag Current Card card' : 'Drag Current List list' })).toBeDisabled();
  expect(registry.dragDisabled).toBe(true); expect(registry.dropDisabled).toBe(true);
  expect(original.isConnected).toBe(true);
  expect(registry.dragNode).toHaveBeenCalledTimes(1); expect(registry.dropNode).toHaveBeenCalledTimes(1);
  view.unmount();
  expect(registry.dragNode).toHaveBeenLastCalledWith(null); expect(registry.dropNode).toHaveBeenLastCalledWith(null);
  expect(registry.dragNode).toHaveBeenCalledTimes(2); expect(registry.dropNode).toHaveBeenCalledTimes(2);
});
