import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { expect, test } from './releaseTest';
import { waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// Explicit alternate config: never discovered by the ordinary .spec.ts suite.
// The CI shell fixture supplies a real uploaded/published image and owns the
// exact API/Worker lifecycle. Only the first committed response is dropped.
for (const width of [1280, 390]) {
  test(`PRD-04 real publication selection, original recovery and image-backed copy at ${width}px`, async ({ page, context }) => {
    test.setTimeout(150_000);
    expect(process.env.CI).toBe('true');
    const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE;
    expect(path).toBeTruthy();
    const fixture = JSON.parse(readFileSync(path!, 'utf8')) as { email: string; password: string; organizationId: string; boardId: string; cardId: string };
    for (const id of [fixture.organizationId, fixture.boardId, fixture.cardId]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
    await page.setViewportSize({ width, height: 844 });
    const login = await context.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' },
      data: { email: fixture.email, password: fixture.password } });
    expect(login.status()).toBe(200);
    const board = fixture.boardId, org = fixture.organizationId;
    await waitForBoardDelivery(context.request, board);
    const cardPath = `/app/${org}/boards/${board}/cards/${fixture.cardId}`;
    const reads = trackBoardReads(page, board, cardPath);
    await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    const attempts: { key: string | undefined; body: string | null }[] = [];
    await page.route(`**/boards/${board}/background/image`, async route => {
      expect(route.request().method()).toBe('POST');
      const result = await route.fetch(); expect(result.status()).toBe(200);
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      if (attempts.length === 1) await route.abort('failed');
      else { expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]); await route.fulfill({ response: result }); }
    });
    const review = page.getByRole('button', { name: 'Review Board background images', exact: true });
    await expect(review).toBeEnabled(); await review.press('Enter');
    await page.getByRole('button', { name: 'Use Private original.png as Board background', exact: true }).press('Enter');
    const confirm = page.getByRole('button', { name: 'Confirm Board background image', exact: true });
    await expect(confirm).toBeEnabled(); await expect(confirm).toBeFocused(); await confirm.press('Enter');
    const retry = page.getByRole('button', { name: 'Retry original Board background change', exact: true });
    await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
    for (const name of ['Save card', 'Review Card cover', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
    await retry.press('Enter'); await expect(page.getByText('Board background updated.', { exact: true })).toBeVisible();
    await expect(review).toBeFocused(); expect(attempts).toHaveLength(2);
    const sourceResponse = await context.request.get(`/boards/${board}`); expect(sourceResponse.status()).toBe(200);
    const source = (await sourceResponse.json()).board;
    expect(source.backgroundType).toBe('IMAGE'); expect(source.backgroundValue).toMatch(/^[0-9a-f-]{36}$/);
    const image = page.locator(`img[src="/boards/${board}/background/image?boardVersion=${source.version}"]`);
    await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    await expect(image).toHaveAttribute('alt', '');
    expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    await page.getByRole('button', { name: 'Close', exact: true }).press('Enter');
    const copy = page.getByRole('button', { name: 'Copy Board', exact: true }); await expect(copy).toBeEnabled(); await copy.press('Enter');
    const create = page.getByRole('button', { name: 'Create Board copy', exact: true }); await expect(create).toBeEnabled(); await create.press('Enter');
    const open = page.getByRole('link', { name: 'Open copied Board', exact: true }); await expect(open).toBeVisible(); await expect(open).toBeFocused();
    const href = await open.getAttribute('href'); const target = href!.split('/').at(-1)!; expect(target).not.toBe(board);
    await waitForBoardDelivery(context.request, target);
    const copiedResponse = await context.request.get(`/boards/${target}`); expect(copiedResponse.status()).toBe(200);
    const copied = (await copiedResponse.json()).board;
    expect(copied).toMatchObject({ visibility: 'PRIVATE', backgroundType: 'IMAGE', version: 1 });
    expect(copied.backgroundValue).not.toBe(source.backgroundValue);
    await open.press('Enter'); await expect(page).toHaveURL(new RegExp(`/boards/${target}$`));
    const copiedImage = page.locator(`img[src="/boards/${target}/background/image?boardVersion=1"]`);
    await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  });
}
