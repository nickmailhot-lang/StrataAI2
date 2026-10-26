import assert from 'node:assert/strict';
import { test } from 'node:test';
import { expect } from '@playwright/test';
import { closeAdmittedDisclosure, fillAdmittedInput, openAdmittedReview, pressObservedLocalAction, pressObservedRequest, pressObservedValidation, tabAdmittedFocus } from './keyboardAdmission.ts';

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

class LocalAction extends Locator {
  node = Object.assign(new EventTarget(), { isConnected: true });
  observation;
  disposed = false;
  result = 'original';
  async evaluateHandle(callback) {
    const observed = callback(this.node); this.observation = observed;
    return { evaluate: async fn => fn(observed), dispose: async () => { this.disposed = true; } };
  }
  async focus() {
    this.focuses++;
    if (this.mode === 'replaced') this.node.isConnected = false;
    if (this.mode === 'activated-during-focus') { this.node.dispatchEvent(new Event('click')); this.retired = true; }
  }
  async press(key) {
    assert.equal(key, 'Enter'); assert.equal(this.node.isConnected, true); this.presses++;
    if (this.mode === 'ignored-first' && this.presses === 1) return;
    this.node.dispatchEvent(new Event('click'));
    if (this.mode === 'duplicate') this.node.dispatchEvent(new Event('click'));
    if (this.mode !== 'wrong-result') this.result = 'current';
    this.retired = true;
  }
}
test('local action observes one native activation and removes its listener', async () => {
  const button = new LocalAction('normal'); await pressObservedLocalAction(button);
  assert.equal(button.presses, 1); assert.equal(button.observation.state.count, 1); assert.equal(button.disposed, true);
  button.node.dispatchEvent(new Event('click')); assert.equal(button.observation.state.count, 1);
});
test('only an ignored local keypress is readmitted', { timeout: 10_000 }, async () => {
  const button = new LocalAction('ignored-first'); await pressObservedLocalAction(button);
  assert.equal(button.presses, 2); assert.equal(button.observation.state.count, 1);
});
test('local activation during focus cannot send another Enter to a retired control', async () => {
  const button = new LocalAction('activated-during-focus'); await pressObservedLocalAction(button);
  assert.equal(button.presses, 0); assert.equal(button.observation.state.count, 1);
});
test('a wrong consumer result after activation is not repaired by another activation', async () => {
  const button = new LocalAction('wrong-result'); await pressObservedLocalAction(button);
  assert.throws(() => assert.equal(button.result, 'current')); assert.equal(button.presses, 1);
});
test('duplicate local activations remain a failure', { timeout: 10_000 }, async () => {
  const button = new LocalAction('duplicate'); await assert.rejects(pressObservedLocalAction(button));
  assert.equal(button.presses, 1); assert.equal(button.observation.state.count, 2);
});
test('a replacement local control cannot receive the original activation', { timeout: 10_000 }, async () => {
  const button = new LocalAction('replaced'); await assert.rejects(pressObservedLocalAction(button));
  assert.equal(button.presses, 0); assert.equal(button.observation.state.count, 0);
});

class TraversalDestination {
  _apiName = 'Locator';
  focused = false;
  async focus() { assert.fail('Keyboard traversal must never focus its destination'); }
  async _expect() { return { matches: this.focused, received: this.focused, log: [], timedOut: !this.focused }; }
}
class TraversalOrigin {
  _apiName = 'Locator';
  focused = false;
  presses = 0;
  constructor(destination, key, mode = 'normal') { Object.assign(this, { destination, key, mode }); }
  async focus() { this.focused = true; this.destination.focused = false; }
  async _expect(expression) {
    const matches = expression !== 'to.be.focused' || this.focused;
    return { matches, received: matches, log: [], timedOut: !matches };
  }
  async press(key) {
    assert.equal(key, this.key); this.presses++;
    if (this.mode === 'never-arrives' || this.mode === 'ignored-first' && this.presses === 1) return;
    this.focused = false; this.destination.focused = true;
  }
}
test('read-only Tab proves native traversal without focusing its destination', async () => {
  const destination = new TraversalDestination(), origin = new TraversalOrigin(destination, 'Tab');
  await tabAdmittedFocus(origin, destination, 'Tab');
  assert.equal(destination.focused, true); assert.equal(origin.presses, 1);
});
test('an unobserved Shift+Tab repeats from its admitted origin only', async () => {
  const destination = new TraversalDestination(), origin = new TraversalOrigin(destination, 'Shift+Tab', 'ignored-first');
  await tabAdmittedFocus(origin, destination, 'Shift+Tab');
  assert.equal(destination.focused, true); assert.equal(origin.presses, 2);
});
test('broken keyboard order still fails within the original five-second budget', { timeout: 10_000 }, async () => {
  const destination = new TraversalDestination(), origin = new TraversalOrigin(destination, 'Tab', 'never-arrives');
  await assert.rejects(tabAdmittedFocus(origin, destination, 'Tab'));
  assert.equal(destination.focused, false); assert.ok(origin.presses > 0);
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

class ValidationForm {
  node = Object.assign(new EventTarget(), { nodeName: 'FORM', isConnected: true });
  submissions = 0;
  disposed = false;
  observation;
  constructor() { this.node.addEventListener('submit', () => { this.submissions++; }); }
  async evaluateHandle(callback) {
    const observed = callback(this.node); this.observation = observed;
    return { evaluate: async fn => fn(observed), dispose: async () => { this.disposed = true; } };
  }
}
class ValidationButton extends Locator {
  constructor(form, mode = 'normal') { super(mode); this.form = form; }
  async focus() {
    this.focuses++;
    if (this.mode === 'retired-form') this.form.node.isConnected = false;
    if (this.mode === 'submit-during-focus') {
      this.form.node.dispatchEvent(new Event('submit')); this.retired = true;
    }
  }
  async press(key) {
    assert.equal(key, 'Enter'); assert.equal(this.form.node.isConnected, true);
    this.presses++;
    if (this.mode === 'ignored-first' && this.presses === 1) return;
    this.form.node.dispatchEvent(new Event('submit'));
    if (this.mode === 'duplicate-submit') this.form.node.dispatchEvent(new Event('submit'));
    if (this.mode === 'retire-after-submit') { this.form.node.isConnected = false; this.retired = true; }
  }
}

test('local validation observes one native submit and removes its listener', async () => {
  const form = new ValidationForm(), button = new ValidationButton(form);
  await pressObservedValidation(button, form);
  assert.equal(button.presses, 1); assert.equal(form.submissions, 1); assert.equal(form.disposed, true);
  form.node.dispatchEvent(new Event('submit'));
  assert.equal(form.observation.state.count, 1);
});
test('local validation retries only a keypress that emitted no submit', { timeout: 10_000 }, async () => {
  const form = new ValidationForm(), button = new ValidationButton(form, 'ignored-first');
  await pressObservedValidation(button, form);
  assert.equal(button.presses, 2); assert.equal(form.submissions, 1); assert.equal(form.disposed, true);
});
test('an observed validation submit during focus cannot dispatch another Enter', { timeout: 10_000 }, async () => {
  const form = new ValidationForm(), button = new ValidationButton(form, 'submit-during-focus');
  await pressObservedValidation(button, form);
  assert.equal(button.presses, 0); assert.equal(form.submissions, 1); assert.equal(form.disposed, true);
});
test('a submit that retires its form is not replayed while its result is pending', async () => {
  const form = new ValidationForm(), button = new ValidationButton(form, 'retire-after-submit');
  await pressObservedValidation(button, form);
  assert.equal(button.presses, 1); assert.equal(form.submissions, 1); assert.equal(form.disposed, true);
});
test('a retired validation form cannot send a key to a replacement control', { timeout: 12_000 }, async () => {
  const form = new ValidationForm(), button = new ValidationButton(form, 'retired-form');
  await assert.rejects(pressObservedValidation(button, form));
  assert.equal(button.presses, 0); assert.equal(form.submissions, 0); assert.equal(form.disposed, true);
});
test('duplicate native submits remain a failure without another activation', { timeout: 12_000 }, async () => {
  const form = new ValidationForm(), button = new ValidationButton(form, 'duplicate-submit');
  await assert.rejects(pressObservedValidation(button, form));
  assert.equal(button.presses, 1); assert.equal(form.submissions, 2); assert.equal(form.disposed, true);
});

test('a foreground refresh replacing a focused review launcher re-admits focus before one activation', async () => {
  const region = new ReviewRegion();
  class RefreshingReviewLauncher extends ReviewLauncher {
    generation = 0;
    focusedGeneration = -1;
    async focus() {
      this.focuses++;
      this.focusedGeneration = this.generation;
      // Protected metadata refresh retires the focused node before Enter.
      if (this.focuses === 1) this.generation++;
    }
    async _expect(expression) {
      const matches = !this.region.visible && (expression !== 'to.be.focused' || this.focusedGeneration === this.generation);
      return { matches, received: matches, log: [], timedOut: !matches };
    }
    async press(key) {
      assert.equal(this.focusedGeneration, this.generation, 'Never activate a replacement without fresh focus');
      await super.press(key);
    }
  }
  const launcher = new RefreshingReviewLauncher(region);
  await openAdmittedReview(launcher, region);
  assert.equal(region.visible, true);
  assert.equal(launcher.focuses, 2);
  assert.equal(launcher.presses, 1);
});

class DisclosureLauncher extends ReviewLauncher {
  open = true;
  generation = 0;
  focusedGeneration = -1;
  async getAttribute() { return this.open ? '' : null; }
  async focus() {
    this.focuses++; this.focusedGeneration = this.generation;
    if (this.mode === 'replaced-during-focus' && this.focuses === 1) this.generation++;
    if (this.mode === 'closed-during-focus') this.open = false;
  }
  async _expect(expression) {
    const matches = expression !== 'to.be.focused' || this.focusedGeneration === this.generation;
    return { matches, received: matches, log: [], timedOut: !matches };
  }
  async press(key) {
    assert.equal(key, 'Enter'); assert.equal(this.focusedGeneration, this.generation);
    this.presses++; this.open = !this.open;
  }
}
test('disclosure replacement requires fresh focus and exactly one native collapse', async () => {
  const control = new DisclosureLauncher(new ReviewRegion(), 'replaced-during-focus');
  await closeAdmittedDisclosure(control, control);
  assert.equal(control.open, false); assert.equal(control.focuses, 2); assert.equal(control.presses, 1);
});
test('an already-collapsed disclosure is not reopened', async () => {
  const control = new DisclosureLauncher(new ReviewRegion()); control.open = false;
  await closeAdmittedDisclosure(control, control);
  assert.equal(control.focuses, 0); assert.equal(control.presses, 0);
});
test('a refresh collapsing the disclosure during focus cannot toggle it open again', async () => {
  const control = new DisclosureLauncher(new ReviewRegion(), 'closed-during-focus');
  await closeAdmittedDisclosure(control, control);
  assert.equal(control.open, false); assert.equal(control.presses, 0);
});
