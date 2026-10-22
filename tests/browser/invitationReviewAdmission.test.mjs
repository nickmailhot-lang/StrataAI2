import assert from 'node:assert/strict';
import { test } from 'node:test';
import { expect } from '@playwright/test';
import { openAdmittedReview, openFocusedInvitationReview } from './keyboardAdmission.ts';

// Actual Playwright assertions/polling drive this isolated Locator protocol.
// Routed browser coverage separately proves the real transition/consumer.
class Locator {
  _apiName = 'Locator';
  constructor(state, role) { this.state = state; this.role = role; }
  async isVisible() { return this.role !== 'dialog' || this.state.open; }
  async isEnabled() { return true; }
  getByRole(role, options) {
    assert.equal(this.role, 'dialog'); assert.equal(role, 'button');
    assert.deepEqual(options, { name: 'Cancel', exact: true });
    return new Locator(this.state, 'cancel');
  }
  async focus() {
    assert.equal(this.role, 'launcher', 'The fixture must never force Cancel focus');
    this.state.launcherFocus++; this.state.focus = 'launcher';
  }
  async press(key) {
    assert.equal(this.role, 'launcher', 'Only read-only opening is permitted');
    assert.equal(key, 'Enter'); this.state.opens++; this.state.open = true;
    this.state.focus = this.state.mode === 'wrong-focus' ? 'confirm' : 'cancel';
  }
  async _expect(expression) {
    if (this.role === 'cancel' && this.state.mode === 'retire-first' && this.state.opens === 1 && !this.state.retired) {
      this.state.open = false; this.state.retired = true; this.state.focus = 'refresh';
    }
    const matches = expression === 'to.be.visible' ? await this.isVisible()
      : expression === 'to.be.focused' ? this.state.open && this.state.focus === this.role
        : expression === 'to.be.enabled';
    return { matches, received: this.state.focus, log: [], timedOut: !matches };
  }
}
function fixture(mode) {
  const state = { mode, opens: 0, open: false, retired: false, focus: '', launcherFocus: 0 };
  return { state, launcher: new Locator(state, 'launcher'), dialog: new Locator(state, 'dialog') };
}
test('stable invitation review uses the product default focus and one native opening', async () => {
  const f = fixture('stable'); await openFocusedInvitationReview(f.launcher, f.dialog);
  assert.equal(f.state.opens, 1); assert.equal(f.state.focus, 'cancel');
});
test('a protected refresh can retire transient visibility before the original focus assertion', async () => {
  const f = fixture('retire-first'); await openAdmittedReview(f.launcher, f.dialog);
  await assert.rejects(expect(f.dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused({ timeout: 500 }));
  assert.equal(f.state.opens, 1); assert.equal(f.state.open, false);
});
test('only the retired read-only review reopens; Cancel is never focused by the fixture', async () => {
  const f = fixture('retire-first'); await openFocusedInvitationReview(f.launcher, f.dialog);
  assert.equal(f.state.opens, 2); assert.equal(f.state.focus, 'cancel'); assert.equal(f.state.retired, true);
});
test('an already focused review cannot receive an extra opening key', async () => {
  const f = fixture('stable'); f.state.open = true; f.state.focus = 'cancel';
  await openFocusedInvitationReview(f.launcher, f.dialog); assert.equal(f.state.opens, 0); assert.equal(f.state.launcherFocus, 0);
});
test('a genuinely wrong default focus still fails within the original five-second budget', { timeout: 10_000 }, async () => {
  const f = fixture('wrong-focus'); await assert.rejects(openFocusedInvitationReview(f.launcher, f.dialog));
  assert.equal(f.state.opens, 1); assert.equal(f.state.focus, 'confirm');
});
