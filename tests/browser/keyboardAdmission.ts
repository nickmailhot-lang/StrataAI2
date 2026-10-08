import type { Locator } from '@playwright/test';
import { expect } from './releaseTest';

// Focusing a page/control can start a protected foreground read. Establish
// current enabled focus before the one activation keypress; retry only focus.
export async function pressAdmittedAction(button: Locator, key: 'Enter' | 'Space' = 'Enter') {
  await expect(async () => {
    await expect(button).toBeEnabled({ timeout: 500 });
    await button.focus({ timeout: 500 });
    await expect(button).toBeFocused({ timeout: 500 });
    await expect(button).toBeEnabled({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
  await button.press(key);
}
