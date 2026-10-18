import assert from 'node:assert/strict';
import { test } from 'node:test';
import { expect } from '@playwright/test';
import { fillAdmittedInput, openAdmittedReview, pressObservedRequest } from './keyboardAdmission.ts';

// The real Playwright expectations/polling drive this isolated Locator protocol
// fixture. Product browser cases separately prove native UI and HTTP behavior.
class Locator {
  _apiName = 'Locator';
  count = 1;
  presses = 0;
  focuses = 0;
  retired = false;
  constructor(mode) { this.mode = mode; }
  async _expect(expression) {
    const matches = !this.retired;
    if (expression === 'to.be.enabled' && this.mode === 'observed-during-enabled') {
      this.count = 2; this.retired = true;
    }
    return { matches, received: this.retired ? 'retired' : 'admitted', log: [], timedOut: !matches };
  }
  async focus() {
    if (this.retired) throw new Error('Control has retired');
    this.focuses++;
    if (this.mode === 'observed-during-focus' || this.mode === 'excess-request') {
      this.count = this.mode === 'excess-request' ? 3 : 2;
      this.retired = true;
    }
  }
  async press(key) {
    assert.equal(key, 'Enter'); assert.equal(this.retired, false);
    this.presses++;
    if (this.mode === 'unadmitted-keypress' && this.presses === 1) return;
    this.count++; this.retired = true;
  }
}

test('an admitted action dispatches exactly one Enter and stops at the expected request', async () => {
  const button = new Locator('normal');
  await pressObservedRequest(button, () => button.count, 2);
  assert.equal(button.presses, 1); assert.equal(button.count, 2);
});

test('an ignored keypress admits one request and stops once that request starts', { timeout: 10_000 }, async () => {
  const button = new Locator('unadmitted-keypress');
  await pressObservedRequest(button, () => button.count, 2);
  assert.equal(button.presses, 2); assert.equal(button.focuses, 2); assert.equal(button.count, 2);
});

for (const mode of ['observed-during-enabled', 'observed-during-focus']) {
  test(`request observation retires the launcher while admission is waiting: ${mode}`, { timeout: 10_000 }, async () => {
    const button = new Locator(mode);
    await pressObservedRequest(button, () => button.count, 2);
    assert.equal(button.count, 2); assert.equal(button.presses, 0);
    assert.equal(button.focuses, mode === 'observed-during-focus' ? 1 : 0);
  });
}

test('an extra request remains a failure even when the launcher has retired', { timeout: 12_000 }, async () => {
  const button = new Locator('excess-request');
  await assert.rejects(pressObservedRequest(button, () => button.count, 2));
  assert.equal(button.count, 3); assert.equal(button.presses, 0);
});

test('an unexpected initial observation cannot trigger another Enter', async () => {
  const button = new Locator('normal'); button.count = 2;
  await assert.rejects(pressObservedRequest(button, () => button.count, 2));
  assert.equal(button.focuses, 0); assert.equal(button.presses, 0);
});

class ReviewRegion {
  _apiName = 'Locator';
  visible = false;
  async isVisible() { return this.visible; }
  async _expect() { return { matches: this.visible, received: this.visible, log: [], timedOut: !this.visible }; }
}
class ReviewLauncher {
  _apiName = 'Locator';
  focuses = 0;
  presses = 0;
  focused = false;
  constructor(region, mode = 'normal') { this.region = region; this.mode = mode; }
  async isVisible() { return !this.region.visible; }
  async isEnabled() { return true; }
  async focus() {
    this.focuses++; this.focused = true;
    if (this.mode === 'opens-during-focus') this.region.visible = true;
  }
  async _expect(expression) {
    const matches = !this.region.visible && (expression !== 'to.be.focused' || this.focused);
    return { matches, received: matches, log: [], timedOut: !matches };
  }
  async press(key) {
    assert.equal(key, 'Enter'); this.presses++;
    if (this.mode === 'never-opens' || this.mode === 'ignored-first' && this.presses === 1) return;
    this.region.visible = true;
  }
}

test('nonmutating activity review observes its inline region before returning', async () => {
  const region = new ReviewRegion(); const launcher = new ReviewLauncher(region);
  await openAdmittedReview(launcher, region);
  assert.equal(region.visible, true); assert.equal(launcher.presses, 1);
});
test('an ignored review activation is retried only until its region opens', async () => {
  const region = new ReviewRegion(); const launcher = new ReviewLauncher(region, 'ignored-first');
  await openAdmittedReview(launcher, region);
  assert.equal(region.visible, true); assert.equal(launcher.presses, 2);
});
test('an already-open region does not require its retired launcher', async () => {
  const region = new ReviewRegion(); region.visible = true; const launcher = new ReviewLauncher(region);
  await openAdmittedReview(launcher, region);
  assert.equal(launcher.focuses, 0); assert.equal(launcher.presses, 0);
});
test('review opening during focus cannot activate the retired launcher', async () => {
  const region = new ReviewRegion(); const launcher = new ReviewLauncher(region, 'opens-during-focus');
  await openAdmittedReview(launcher, region);
  assert.equal(region.visible, true); assert.equal(launcher.presses, 0);
});
test('an unopened review remains a failure within the original admission budget', { timeout: 10_000 }, async () => {
  const region = new ReviewRegion(); const launcher = new ReviewLauncher(region, 'never-opens');
  await assert.rejects(openAdmittedReview(launcher, region));
  assert.equal(region.visible, false); assert.ok(launcher.presses > 0);
});

class InputLocator extends Locator {
  fills = 0;
  node = Object.assign(new EventTarget(), { value: '', isConnected: true });
  observation;
  async evaluateHandle(callback) {
    const observation = callback(this.node); this.observation = observation;
    return { evaluate: async fn => fn(observation), dispose: async () => {} };
  }
  async focus() {
    this.focuses++;
    if (this.mode === 'replaced') this.node.isConnected = false;
  }
  async fill(value) {
    this.fills++;
    if (this.mode === 'unadmitted-input' && this.fills === 1) return;
    this.node.value = value; this.node.dispatchEvent(new Event('input'));
    if (this.mode === 'cleared-after-input') this.node.value = '';
    if (this.mode === 'excess-input') this.node.dispatchEvent(new Event('input'));
  }
  async _expect(expression, options) {
    if (expression !== 'to.have.value') return super._expect(expression, options);
    const matches = this.node.value === options.expectedText[0].string;
    return { matches, received: this.node.value, log: [], timedOut: !matches };
  }
}

test('admitted text dispatches one native input event and one insertion', async () => {
  const input = new InputLocator('normal');
  await fillAdmittedInput(input, 'Inspect'); await expect(input).toHaveValue('Inspect');
  assert.equal(input.fills, 1); assert.equal(input.observation.state.count, 1);
});
test('only an ignored insertion with no input event can be attempted again', { timeout: 10_000 }, async () => {
  const input = new InputLocator('unadmitted-input');
  await fillAdmittedInput(input, 'Inspect'); await expect(input).toHaveValue('Inspect');
  assert.equal(input.fills, 2); assert.equal(input.observation.state.count, 1);
});
test('a draft cleared after native input still fails its value assertion without refilling', async () => {
  const input = new InputLocator('cleared-after-input');
  await fillAdmittedInput(input, 'Inspect');
  await assert.rejects(expect(input).toHaveValue('Inspect', { timeout: 100 }));
  assert.equal(input.fills, 1); assert.equal(input.observation.state.count, 1);
});
test('a replacement control cannot receive the earlier input intent', { timeout: 10_000 }, async () => {
  const input = new InputLocator('replaced');
  await assert.rejects(fillAdmittedInput(input, 'Inspect'));
  assert.equal(input.fills, 0); assert.equal(input.observation.state.count, 0);
});
test('extra native input remains a failure and cannot trigger another insertion', { timeout: 10_000 }, async () => {
  const input = new InputLocator('excess-input');
  await assert.rejects(fillAdmittedInput(input, 'Inspect'));
  assert.equal(input.fills, 1); assert.equal(input.observation.state.count, 2);
});
