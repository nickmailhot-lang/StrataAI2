import type { Locator } from '@playwright/test';
import { expect } from './releaseTest';

// Focusing a page/control can start a protected foreground read. Establish
// current enabled focus before the one activation keypress; retry only focus.
export async function focusAdmittedControl(button: Locator) {
  await expect(async () => {
    await expect(button).toBeEnabled({ timeout: 500 });
    await button.focus({ timeout: 500 });
    await expect(button).toBeFocused({ timeout: 500 });
    await expect(button).toBeEnabled({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
}

export async function pressAdmittedAction(button: Locator, key: 'Enter' | 'Space' = 'Enter') {
  await focusAdmittedControl(button);
  await button.press(key);
}

// A live read can disable the control after focus and send Enter to the body.
// Retry keyboard admission only until this one request starts. Never activate
// again once the route observer advances, even while its response is pending.
// Response, receipt, state and exact request-count assertions belong to callers.
export async function pressObservedRequest(button: Locator, requestCount: () => number, expectedCount: number) {
  expect(requestCount()).toBe(expectedCount - 1);
  await expect(async () => {
    if (requestCount() === expectedCount - 1) {
      await focusAdmittedControl(button);
      // The observer can advance while admission waits; recheck before Enter.
      if (requestCount() === expectedCount - 1) await button.press('Enter', { timeout: 500 });
    }
    await expect.poll(requestCount, { timeout: 500 }).toBe(expectedCount);
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
