import { test } from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { installCapacityScrollDiagnostics } from './capacityScrollDiagnostics.ts';

function fixture() {
  const calls = [], listeners = new Map();
  class Element {
    constructor(owned = true) { this.owned = owned; this.axis = 'horizontal'; this.offset = 40; this.scrollTop = 7; }
    getAttribute(name) { return name === 'data-kanban-scroll-axis' ? this.axis : 'private-content'; }
    hasAttribute() { return this.owned; }
    closest() { return this; }
    get scrollLeft() { return this.offset; }
    set scrollLeft(value) { calls.push(['set', this, value]); this.offset = value; }
    scrollBy(...args) {
      calls.push(['by', this, ...args]); if (args[0] === 'throw') throw new Error('Native failure');
      if (typeof args[0] === 'object' && args[0] !== null) void args[0].left;
      return 'native-result';
    }
    scrollIntoView(...args) { calls.push(['view', this, ...args]); return 'native-view'; }
  }
  const window = { performance: { now: () => 12 } };
  const context = vm.createContext({ Element, window, document: { addEventListener: (type, fn, capture) => listeners.set(capture ? type + ':capture' : type, fn) } });
  vm.runInContext(`(${installCapacityScrollDiagnostics.toString()})()`, context);
  const samples = () => JSON.parse(JSON.stringify(window.__capacityScrollDiagnostics));
  return { Element, window, context, calls, listeners, samples };
}

test('both native scrollBy overloads preserve receiver, arguments, return and exceptions', () => {
  const f = fixture(), node = new f.Element(), options = { left: -211, top: 0, behavior: 'instant' };
  assert.equal(node.scrollBy(-8, 0), 'native-result');
  assert.equal(node.scrollBy(options), 'native-result');
  assert.equal(f.calls[0][1], node); assert.deepEqual(f.calls[0].slice(2), [-8, 0]);
  assert.equal(f.calls[1][2], options);
  assert.throws(() => node.scrollBy('throw'), /Native failure/);
  assert.deepEqual(f.samples().samples.map(x => x.arguments), [[-8, 0], [-211, 0], [null, null]]);
});

test('direct assignments and focus scrolling delegate unchanged and retain only numerical evidence', () => {
  const f = fixture(), node = new f.Element();
  f.listeners.get('pointermove')({ clientX: 195, clientY: 471, privateBody: 'secret' });
  node.scrollLeft = 64945; const options = { block: 'nearest' };
  assert.equal(node.scrollIntoView(options), 'native-view');
  assert.equal(node.scrollLeft, 64945); assert.equal(f.calls[1][2], options);
  const evidence = f.samples();
  assert.deepEqual(evidence.samples[0], { at: 12, kind: 'scrollLeft', axis: 'horizontal', left: 40, top: 7,
    arguments: [64945], pointer: { x: 195, y: 471 }, viewport: null });
  assert.equal(evidence.samples[1].kind, 'scrollIntoView');
  assert.doesNotMatch(JSON.stringify(evidence), /secret|private-content|nearest/);
});

test('bounded evidence excludes unowned surfaces and repeated installation does not wrap twice', () => {
  const f = fixture(), node = new f.Element(), other = new f.Element(false);
  other.scrollBy(1, 2); assert.equal(f.samples().samples.length, 0);
  vm.runInContext(`(${installCapacityScrollDiagnostics.toString()})()`, f.context);
  for (let i = 0; i < 2050; i++) node.scrollBy(i, 0);
  const evidence = f.samples(); assert.equal(evidence.samples.length, 2048); assert.equal(evidence.dropped, 2);
  assert.equal(evidence.samples[0].arguments[0], 2); assert.equal(f.calls.length, 2051);
});

test('a diagnostic read failure does not prevent the original browser operation', () => {
  const f = fixture(), node = new f.Element(); node.getAttribute = () => { throw new Error('Diagnostic failure'); };
  assert.equal(node.scrollBy(1, 2), 'native-result'); node.scrollLeft = 80;
  assert.equal(node.scrollLeft, 80); assert.equal(f.calls.length, 2); assert.equal(f.samples().samples.length, 0);
  node.closest = () => { throw new Error('Diagnostic lookup failure'); };
  assert.equal(node.scrollIntoView(), 'native-view');
});

test('option accessors are evaluated only by the original operation', () => {
  const f = fixture(), node = new f.Element(); let reads = 0;
  node.scrollBy({ get left() { reads++; return 211; }, top: 0 });
  assert.equal(reads, 1); assert.deepEqual(f.samples().samples[0].arguments, [null, 0]);
});

test('scroll geometry retains only finite viewport numbers and never private rectangle fields', () => {
  const f = fixture(), node = new f.Element();
  node.clientWidth = 342; node.clientHeight = 500;
  node.getBoundingClientRect = () => ({ left: 24, right: 366, top: 80, bottom: Infinity, privateBody: 'secret' });
  node.scrollBy(-8, 0);
  assert.equal(f.samples().schema, 3);
  assert.deepEqual(f.samples().samples[0].viewport, { left: 24, right: 366, top: 80, bottom: null, width: 342, height: 500 });
  assert.doesNotMatch(JSON.stringify(f.samples()), /secret|privateBody/);
  node.getBoundingClientRect = () => { throw new Error('Geometry failure'); };
  assert.equal(node.scrollBy(-8, 0), 'native-result');
  assert.equal(f.samples().samples[1].viewport, null);
});

test('capture-only movement distinguishes withdrawn bubbling without changing the original scroll pointer', () => {
  const f = fixture(), node = new f.Element();
  f.listeners.get('pointermove')({ clientX: 30, clientY: 90, buttons: 1 });
  const event = { clientX: 449, clientY: 470.5, buttons: 1, target: { privateBody: 'secret' }, stopPropagation() { throw new Error('Do not alter input'); } };
  f.listeners.get('pointermove:capture')(event);
  f.listeners.get('mousemove:capture')(event);
  node.scrollBy(-8, 0);
  const evidence = f.samples();
  assert.deepEqual(evidence.samples[0].pointer, { x: 30, y: 90 });
  assert.deepEqual(evidence.input.pointerCapture, { count: 1, last: { at: 12, x: 449, y: 470.5, buttons: 1 } });
  assert.deepEqual(evidence.input.pointerBubble.last, { at: 12, x: 30, y: 90, buttons: 1 });
  assert.equal(evidence.input.mouseBubble.count, 0);
  assert.doesNotMatch(JSON.stringify(evidence), /secret|privateBody|target|stopPropagation/);
});

test('input channels keep only their latest finite point and observe release/cancellation without retaining event objects', () => {
  const f = fixture();
  for (let i = 0; i < 3000; i++) f.listeners.get('mousemove')({ clientX: i, clientY: Infinity, buttons: 1, privateBody: 'secret' });
  f.listeners.get('pointercancel:capture')({ clientX: NaN, clientY: 9, buttons: 0 });
  f.listeners.get('pointerup:capture')({ clientX: 7, clientY: 9, buttons: 0 });
  const evidence = f.samples();
  assert.equal(evidence.input.mouseBubble.count, 3000);
  assert.deepEqual(evidence.input.mouseBubble.last, { at: 12, x: 2999, y: null, buttons: 1 });
  assert.equal(evidence.input.pointerCancel.last.x, null);
  assert.equal(evidence.input.pointerUp.last.buttons, 0);
  assert.equal(evidence.samples.length, 0);
  assert.doesNotMatch(JSON.stringify(evidence), /secret|privateBody/);
});
