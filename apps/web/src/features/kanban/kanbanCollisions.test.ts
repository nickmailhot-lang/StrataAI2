import { expect, it } from 'vitest';
import { pointerWithin, type CollisionDetection } from '@dnd-kit/core';
import { kanbanCollisions } from './kanbanCollisions';

type Args = Parameters<CollisionDetection>[0];
function fixture(activeId = 'card:source'): Args {
  const source = { top: 40, left: 40, right: 60, bottom: 60, width: 20, height: 20 };
  const destination = { top: 0, left: 0, right: 100, bottom: 100, width: 100, height: 100 };
  const rects = new Map([[activeId, source], ['card-end:destination', destination], ['list', source]]);
  return { active: { id: activeId, data: { current: {} }, rect: { current: { initial: source, translated: source } } },
    collisionRect: source, pointerCoordinates: { x: 50, y: 50 }, droppableRects: rects,
    droppableContainers: [...rects].map(([id, rect]) => ({ id, key: id, disabled: false,
      data: { current: {} }, node: { current: null }, rect: { current: rect } })) };
}

it('admits the actual destination when a translated Card source overlaps its pointer drop surface', () => {
  const args = fixture();
  // The real library prefers the smaller source rectangle when both contain
  // the point. That no-op source must not suppress an admitted destination.
  expect(pointerWithin({ ...args, droppableContainers: args.droppableContainers.filter(c => c.id !== 'list') })[0].id).toBe('card:source');
  expect(kanbanCollisions(args).map(c => c.id)).toEqual(['card-end:destination']);
});

it('does not invent a pointer destination outside current target geometry or when only the source remains', () => {
  const args = fixture();
  expect(kanbanCollisions({ ...args, pointerCoordinates: { x: 200, y: 200 } })).toEqual([]);
  expect(kanbanCollisions({ ...args, droppableContainers: args.droppableContainers.filter(c => c.id === args.active.id) })).toEqual([]);
});

it('preserves the keyboard starting target and keeps List/Card audiences separate', () => {
  expect(kanbanCollisions({ ...fixture(), pointerCoordinates: null })[0].id).toBe('card:source');
  expect(kanbanCollisions(fixture('moving-list')).map(c => c.id)).toEqual(['list']);
});
