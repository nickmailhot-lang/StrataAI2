import { describe, expect, it } from 'vitest';
import { ListKeyboardNavigation, listKeyboardCoordinates } from './listKeyboardCoordinates';

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
  it('advances from the previous keyboard target when the rendered source temporarily lags smooth scrolling', () => {
    const navigation = new ListKeyboardNavigation(); navigation.start();
    const args = fixture();
    let sourceLeft = 336, sourceTop = 10;
    args.context.droppableContainers.getEnabled = () => [
      { id: 'middle', node: { current: { getBoundingClientRect: () => ({ left: sourceLeft, top: sourceTop, width: 320, height: 240 }) } } },
      { id: 'last', node: { current: { getBoundingClientRect: () => ({ left: 672, top: 10, width: 320, height: 240 }) } } },
      { id: 'list-end' },
    ] as unknown as ReturnType<typeof args.context.droppableContainers.getEnabled>;
    expect(navigation.coordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 436, y: 20 });
    sourceLeft = 669; args.currentCoordinates = { x: 436, y: 20 };
    // The source is three pixels behind the last target at this key boundary.
    // A physical-nearest query would repeat that same destination instead.
    expect(navigation.coordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 665, y: 40 });
    sourceLeft = 895; sourceTop = 30; args.currentCoordinates = { x: 665, y: 40 };
    expect(navigation.coordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toBeUndefined();
    expect(navigation.coordinates(new KeyboardEvent('keydown', { code: 'ArrowLeft' }), args)).toEqual({ x: 442, y: 20 });
    navigation.finish(); navigation.start(); sourceLeft = 336; sourceTop = 10; args.currentCoordinates = { x: 100, y: 20 };
    expect(navigation.coordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 436, y: 20 });
  });
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
  it('uses committed column positions after scrolling instead of stale cached targets', () => {
    const args = fixture();
    args.context.droppableContainers.getEnabled = () => [
      { id: 'middle', node: { current: { getBoundingClientRect: () => ({ left: 337.5, top: 10, width: 320, height: 240 }) } } },
      { id: 'last', node: { current: { getBoundingClientRect: () => ({ left: 672, top: 10, width: 320, height: 240 }) } } },
    ] as unknown as ReturnType<typeof args.context.droppableContainers.getEnabled>;
    args.context.droppableRects.set('last', { ...args.context.collisionRect!, left: 337.5 });
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 434.5, y: 20 });
  });
  it('advances past an already aligned destination during fractional phone scrolling', () => {
    const args = fixture();
    args.context.collisionRect = { ...args.context.collisionRect!, left: 670.5 };
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 327.5, y: 40 });
    args.context.collisionRect = { ...args.context.collisionRect!, left: 337.5 };
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowLeft' }), args)).toEqual({ x: -237.5, y: 20 });
  });
  it('uses the rendered source center when its collision rect lags horizontal and vertical scroll', () => {
    const args = fixture();
    args.context.collisionRect = { ...args.context.collisionRect!, left: 672 };
    args.context.droppableContainers.getEnabled = () => [
      { id: 'middle', node: { current: { getBoundingClientRect: () => ({ left: 336, top: 44.5, width: 320, height: 240 }) } } },
      { id: 'last', node: { current: { getBoundingClientRect: () => ({ left: 672, top: 10, width: 320, height: 240 }) } } },
      { id: 'list-end' },
    ] as unknown as ReturnType<typeof args.context.droppableContainers.getEnabled>;
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowRight' }), args)).toEqual({ x: 436, y: -14.5 });
  });
  it('leaves unrelated keys and missing measurements alone', () => {
    const args = fixture();
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'Escape' }), args)).toBeUndefined();
    args.context.collisionRect = null;
    expect(listKeyboardCoordinates(new KeyboardEvent('keydown', { code: 'ArrowLeft' }), args)).toBeUndefined();
  });
});
