import test from 'node:test';
import assert from 'node:assert/strict';
import { armCachedDetailPaint, readCachedDetailPaint } from './cachedDetailPaint.ts';

function browser(t) {
  let clock = 0, deadline; const frames = []; let cleared = false;
  class Element { closest() { return this; } }
  const anchor = new Element(); anchor.pathname = '/cards/current';
  const input = { disabled: false, value: 'Current', hidden: false,
    closest() { return this.hidden ? {} : null; },
    rect: { top: 0, bottom: 40, left: 0, right: 100, width: 100, height: 40 },
    getBoundingClientRect() { return this.rect; } };
  const document = new EventTarget(); document.activeElement = input; document.querySelector = () => input;
  const replacements = { window: { performance: { now: () => clock } }, document, Element,
    innerHeight: 844, innerWidth: 390, requestAnimationFrame: callback => frames.push(callback),
    setTimeout: (callback, duration) => { assert.equal(duration, 5000); deadline = callback; return 1; },
    clearTimeout: identity => { assert.equal(identity, 1); cleared = true; } };
  const originals = new Map(Object.keys(replacements).map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  for (const [key, value] of Object.entries(replacements)) Object.defineProperty(globalThis, key, { value, configurable: true });
  t.after(() => { for (const [key, descriptor] of originals) { if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key]; } });
  return { input, document, frames, cleared: () => cleared, expire: () => deadline(),
    click(path = '/cards/current', time = 10) { clock = time; anchor.pathname = path;
      const event = new Event('click'); Object.defineProperty(event, 'target', { value: anchor }); document.dispatchEvent(event); },
    frame(time) { clock = time; assert.ok(frames.length); frames.shift()(); } };
}

test('only the intended native click starts measurement, and readiness survives a painted frame', async t => {
  const b = browser(t); armCachedDetailPaint({ path: '/cards/current', title: 'Current' });
  b.click('/cards/other'); assert.equal(b.frames.length, 0);
  b.click(); b.frame(30); assert.equal(b.cleared(), false);
  b.frame(50); assert.equal(await readCachedDetailPaint(), 40); assert.equal(b.cleared(), true);
  assert.throws(readCachedDetailPaint, /not armed/);
});

for (const condition of ['disabled', 'wrong title', 'hidden', 'outside viewport', 'unfocused', 'zero size']) {
  test(`cannot certify ${condition} as editable painted readiness`, async t => {
    const b = browser(t);
    if (condition === 'disabled') b.input.disabled = true;
    if (condition === 'wrong title') b.input.value = 'Old draft';
    if (condition === 'hidden') b.input.hidden = true;
    if (condition === 'outside viewport') b.input.rect.top = 900;
    if (condition === 'unfocused') b.document.activeElement = null;
    if (condition === 'zero size') b.input.rect.height = 0;
    armCachedDetailPaint({ path: '/cards/current', title: 'Current' }); b.click(); b.frame(40);
    assert.equal(b.cleared(), false); b.expire();
    assert.equal(await readCachedDetailPaint(), Infinity); assert.equal(b.cleared(), true);
  });
}

test('losing readiness before paint keeps the full original elapsed time', async t => {
  const b = browser(t); armCachedDetailPaint({ path: '/cards/current', title: 'Current' }); b.click(); b.frame(40);
  b.input.disabled = true; b.frame(60); assert.equal(b.cleared(), false);
  b.input.disabled = false; b.frame(100); b.frame(130);
  assert.equal(await readCachedDetailPaint(), 120);
});

test('a missing activation fails on the bounded deadline rather than yielding a passing sample', async t => {
  const b = browser(t); armCachedDetailPaint({ path: '/cards/current', title: 'Current' }); b.expire();
  assert.equal(await readCachedDetailPaint(), Infinity); b.click(); assert.equal(b.frames.length, 0);
});
