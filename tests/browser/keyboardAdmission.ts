import { expect, type Locator } from '@playwright/test';

// Focusing a page/control can start a protected foreground read. Establish
// current enabled focus before the one activation keypress; retry only focus.
export async function focusAdmittedControl(button: Locator, stopWaiting: () => boolean = () => false) {
  await expect(async () => {
    if (stopWaiting()) return;
    await expect(button).toBeEnabled({ timeout: 500 });
    if (stopWaiting()) return;
    await button.focus({ timeout: 500 });
    if (stopWaiting()) return;
    await expect(button).toBeFocused({ timeout: 500 });
    if (stopWaiting()) return;
    await expect(button).toBeEnabled({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
}

export async function pressAdmittedAction(button: Locator, key: 'Enter' | 'Space' = 'Enter') {
  await focusAdmittedControl(button);
  await button.press(key);
}

// Local validation has no HTTP request to observe. Retry only an ignored key
// until the original form emits one native submit; never retry its result.
// Mutation callers must keep their HTTP request/receipt observations instead.
export async function pressObservedValidation(button: Locator, form: Locator) {
  const observation = await form.evaluateHandle(node => {
    if (node.nodeName !== 'FORM') throw new Error('Expected the original validation form');
    const state = { count: 0 };
    const onSubmit = () => { state.count++; };
    node.addEventListener('submit', onSubmit, { capture: true });
    return { node, state, onSubmit };
  });
  try {
    await expect(async () => {
      if (await observation.evaluate(o => o.state.count) === 0) {
        expect(await observation.evaluate(o => o.node.isConnected)).toBe(true);
        await expect(button).toBeEnabled({ timeout: 500 });
        if (await observation.evaluate(o => o.state.count) === 0) {
          await button.focus({ timeout: 500 });
          await expect(button).toBeFocused({ timeout: 500 });
          await expect(button).toBeEnabled({ timeout: 500 });
          if (await observation.evaluate(o => o.state.count) === 0) {
            expect(await observation.evaluate(o => o.node.isConnected)).toBe(true);
            await button.press('Enter', { timeout: 500 });
          }
        }
      }
      await expect.poll(() => observation.evaluate(o => o.state.count), { timeout: 500 }).toBe(1);
    }).toPass({ timeout: 5_000 });
  } finally {
    await observation.evaluate(o => o.node.removeEventListener('submit', o.onSubmit, { capture: true }));
    await observation.dispose();
  }
}

// A live read can disable a field between Playwright's actionability check and
// native insertion. Retry only an insertion that dispatched no input event.
// Once text reaches the field, callers' value assertions must detect any loss;
// never refill a cleared draft or carry it into a replacement control.
export async function fillAdmittedInput(input: Locator, value: string) {
  const observation = await input.evaluateHandle(node => {
    const state = { count: 0 };
    const onInput = () => { state.count++; };
    node.addEventListener('input', onInput);
    return { node, state, onInput };
  });
  try {
    await expect(async () => {
      if (await observation.evaluate(o => o.state.count) === 0) {
        expect(await observation.evaluate(o => o.node.isConnected)).toBe(true);
        await focusAdmittedControl(input);
        if (await observation.evaluate(o => o.state.count) === 0) {
          expect(await observation.evaluate(o => o.node.isConnected)).toBe(true);
          await input.fill(value, { timeout: 500 });
        }
      }
      await expect.poll(() => observation.evaluate(o => o.state.count), { timeout: 500 }).toBe(1);
    }).toPass({ timeout: 5_000 });
  } finally {
    await observation.evaluate(o => o.node.removeEventListener('input', o.onInput));
    await observation.dispose();
  }
}

// A live read can disable the control after focus and send Enter to the body.
// Retry keyboard admission only until this one request starts. Never activate
// again once the route observer advances, even while its response is pending.
// Response, receipt, state and exact request-count assertions belong to callers.
export async function pressObservedRequest(button: Locator, requestCount: () => number, expectedCount: number) {
  expect(requestCount()).toBe(expectedCount - 1);
  await expect(async () => {
    if (requestCount() === expectedCount - 1) {
      await focusAdmittedControl(button, () => requestCount() !== expectedCount - 1);
      // The observer can advance while admission waits; recheck before Enter.
      if (requestCount() === expectedCount - 1) await button.press('Enter', { timeout: 500 });
    }
    await expect.poll(requestCount, { timeout: 500 }).toBe(expectedCount);
  }).toPass({ timeout: 5_000 });
}

// Pickup has no mutation side effect. Stop admission when dnd-kit reports the
// active drag, and recheck after focusing so Space cannot toggle an active drag.
export async function startKeyboardDrag(handle: Locator) {
  await expect(async () => {
    if (await handle.getAttribute('aria-pressed') !== 'true') {
      await focusAdmittedControl(handle);
      if (await handle.getAttribute('aria-pressed') !== 'true') await handle.press('Space', { timeout: 500 });
    }
    await expect(handle).toHaveAttribute('aria-pressed', 'true', { timeout: 500 });
  }).toPass({ timeout: 5_000 });
}

// Opening a review has no write side effect. A foreground admission refresh can
// disable the launcher between focus and Enter; observe the actual dialog and
// retry this opening only. Mutation controls must use one activation above.
export async function openAdmittedReview(button: Locator, dialog: Locator) {
  await expect(async () => {
    if (await dialog.isVisible()) return;
    // A successful opening can retire its launcher before the protected read
    // finishes. Observe the target instead of waiting on that retired control.
    if (await button.isVisible() && await button.isEnabled()) {
      await button.focus({ timeout: 500 });
      await expect(button).toBeFocused({ timeout: 500 });
      await expect(button).toBeEnabled({ timeout: 500 });
      await button.press('Enter', { timeout: 500 });
    }
    await expect(dialog).toBeVisible({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
}
