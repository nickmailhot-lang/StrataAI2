import { expect, test } from './releaseTest';

test('ARCH-02-AC-004: UI reports the deployed web and API commit identity', async ({ page, context }) => {
  const metadataResponse = await context.request.get('/build-metadata.json');
  expect(metadataResponse.status()).toBe(200);
  const metadata = await metadataResponse.json();
  expect(metadata.revision).toMatch(/^(?:[a-f0-9]{40}|[a-f0-9]{64})$/);
  expect(metadata.version).toMatch(/^[0-9A-Za-z.+_-]{1,80}$/);
  const runtimeResponse = await context.request.get('/api/runtime');
  expect(runtimeResponse.status()).toBe(200);
  expect(await runtimeResponse.json()).toMatchObject({ revision: metadata.revision, version: metadata.version });
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 844 });
    await page.goto('/login');
    const footer = page.getByRole('contentinfo', { name: 'Application version', exact: true });
    await footer.scrollIntoViewIfNeeded();
    await expect(footer).toBeVisible();
    await expect(footer).toHaveText(`Version ${metadata.version} · Build ${metadata.revision}`);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
});
