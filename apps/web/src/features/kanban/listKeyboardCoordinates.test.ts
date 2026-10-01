import { describe, expect, it } from 'vitest';
import { listKeyboardCoordinates } from './listKeyboardCoordinates';

type Args = Parameters<typeof listKeyboardCoordinates>[1];
function fixture() {
  const rect = (left: number, width: number, top = 10) => ({ left, top, width, height: 240 });
  return {
    active: 'middle', currentCoordinates: { x: 100, y: 20 },
    context: {
      collisionRect: rect(336, 320),
      droppableContainers: { getEnabled: () => [{ id: 'first' }, { id: 'middle' }, { id: 'last' }, { id: 'unmeasured' }, { id: 'list-end' }] },
      droppableRects: new Map([
        ['first', rect(0, 320)], ['middle', rect(336, 320)], ['last', rect(672, 320)],
        ['disabled', rect(500, 20)], ['list-end', rect(1008, 100, 30)],
      ]),
    },
  } as unknown as Args;
}
describe('keyboard list targets', () => {
  it('moves to the next enabled measured column instead of a fixed pixel increment', () => {
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), fixture())).toEqual({ x: 436, y: 20 });
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowLeft' }), fixture())).toEqual({ x: -236, y: 20 });
  });
  it('centers on the narrower end target and stops at the boundary', () => {
    const args = fixture(); args.context.collisionRect = { ...args.context.collisionRect!, left: 672 };
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 326, y: 40 });
    args.context.collisionRect = { ...args.context.collisionRect!, left: 1008, width: 100 };
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toBeUndefined();
  });
  it('leaves unrelated keys and missing measurements alone', () => {
    const args = fixture();
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'Escape' }), args)).toBeUndefined();
    args.context.collisionRect = null;
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowLeft' }), args)).toBeUndefined();
  });
});
