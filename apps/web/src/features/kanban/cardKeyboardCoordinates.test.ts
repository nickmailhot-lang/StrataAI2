import { expect, it } from 'vitest';
import { cardKeyboardCoordinates } from './cardKeyboardCoordinates';
type Args = Parameters<typeof cardKeyboardCoordinates>[1];
function fixture() {
  const rect = (left: number, top: number, height = 80) => ({ left, top, width: 280, height });
  return { active: 'card:source', currentCoordinates: { x: 20, y: 20 }, context: {
    collisionRect: rect(0, 100),
    droppableContainers: { getEnabled: () => ['card:source', 'card:up', 'card:down', 'card:right', 'card:right-low', 'card:far', 'card-end:first', 'list-end', 'card:unmeasured'].map(id => ({ id, node: { current: null } })) },
    droppableRects: new Map([
      ['card:source', rect(0, 100)], ['card:up', rect(0, 0)], ['card:down', rect(0, 200)],
      ['card:right', rect(320, 90)], ['card:right-low', rect(320, 300)], ['card:far', rect(640, 100)],
      ['card-end:first', rect(0, 320, 64)], ['list-end', rect(10, 100)], ['card:disabled', rect(0, 150)],
    ]),
  } } as unknown as Args;
}
it('moves vertically within the list and skips disabled, unmeasured and list targets', () => {
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowUp' }), fixture())).toEqual({ x: 20, y: -80 });
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowDown' }), fixture())).toEqual({ x: 20, y: 120 });
});
it('chooses the nearest vertical position in the next column', () => {
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), fixture())).toEqual({ x: 340, y: 10 });
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowLeft' }), fixture())).toBeUndefined();
});
it('supports the shorter end target and stops at vertical boundaries', () => {
  const args = fixture(); args.context.collisionRect = { ...args.context.collisionRect!, top: 200 };
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowDown' }), args)).toEqual({ x: 20, y: 132 });
  args.context.collisionRect = { ...args.context.collisionRect!, top: 320, height: 64 };
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowDown' }), args)).toBeUndefined();
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'Escape' }), args)).toBeUndefined();
});

it('uses committed target positions after virtual row refinement and excludes the translated source', () => {
  const args = fixture();
  const source = document.createElement('div');
  const destination = document.createElement('div');
  source.getBoundingClientRect = () => ({ left: 0, top: 150, width: 280, height: 80 }) as DOMRect;
  destination.getBoundingClientRect = () => ({ left: 0, top: 221, width: 280, height: 80 }) as DOMRect;
  args.context.droppableContainers.getEnabled = () => [
    { id: 'card:source', node: { current: source } },
    { id: 'card:down', node: { current: destination } },
  ].map(container => ({ ...container, key: String(container.id), disabled: false,
    data: { current: {} }, rect: { current: null } }));
  // Cached destination remains at 200; the source's translated DOM position
  // must not become an eligible destination ahead of the actual next Card.
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowDown' }), args)).toEqual({ x: 20, y: 141 });
});

it('advances beyond the already reached target when smooth scrolling leaves a fractional pixel remainder', () => {
  const args = fixture();
  args.context.droppableRects.set('card:down', { ...args.context.collisionRect!, top: 101.5 });
  args.context.droppableRects.set('card:far', { ...args.context.collisionRect!, top: 210 });
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowDown' }), args)).toEqual({ x: 20, y: 130 });
  args.context.droppableRects.set('card:up', { ...args.context.collisionRect!, top: 98.5 });
  args.context.droppableRects.set('card:far', { ...args.context.collisionRect!, top: -10 });
  expect(cardKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowUp' }), args)).toEqual({ x: 20, y: -90 });
});
