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
