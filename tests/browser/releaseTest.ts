import { test as base } from '@playwright/test';
export { expect, type Page, type WebSocketRoute } from '@playwright/test';

// All release scenarios share the same real API socket peer and Nginx client.
// Their setup issues fewer than 20 sensitive requests per scenario. Give both
// production limiters capacity between scenarios instead of weakening limits
// or retrying an unexpected 429 until an assertion happens to pass.
export const test = base.extend<{ releaseRateBudget: void }>({
  releaseRateBudget: [async ({}, use) => {
    if (process.env.STRATAAI_E2E_RATE_PACING === '1')
      await new Promise(resolve => setTimeout(resolve, 25_000));
    await use();
  }, { auto: true, timeout: 35_000 }],
});
