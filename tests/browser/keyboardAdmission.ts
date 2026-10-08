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
