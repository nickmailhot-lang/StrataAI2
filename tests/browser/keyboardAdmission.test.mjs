import assert from 'node:assert/strict';
import { test } from 'node:test';
import { pressObservedRequest } from './keyboardAdmission.ts';

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
