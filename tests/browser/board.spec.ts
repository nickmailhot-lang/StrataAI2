import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import { expect, test, type Page } from "./releaseTest";
import { focusAdmittedControl } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const viewport of [
  { name: "desktop", width: 1280, height: 720 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "mobile", width: 390, height: 844 },
  { name: "narrow mobile", width: 320, height: 844, firstUseSignIn: true },
  { name: "tablet 200% text", width: 768, height: 1024, firstUseSignIn: true, textZoom: 200 },
]) {
  test(`PRD-01/04/07/08/09: persisted board workflow and isolation (${viewport.name})`, async ({
    page,
    context,
    browser,
  }, info) => {
    const firstUseSignIn = 'firstUseSignIn' in viewport && viewport.firstUseSignIn;
    const textZoom = 'textZoom' in viewport && viewport.textZoom === 200;
    const applyTextZoom = () => {
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.style.fontSize = '32px';
      }, { once: true });
    };
    if (textZoom) await context.addInitScript(applyTextZoom);
    let restoreWorker: (() => void) | undefined;
    try {
    await page.setViewportSize({
      width: viewport.width,
      height: viewport.height,
    });
    async function activate(name: string) {
      const button = page.getByRole("button", { name, exact: true });
      if (viewport.name !== "desktop") {
        await button.focus();
        await expect(button).toBeFocused();
        await button.press("Enter");
      } else await button.click();
    }
    // Opening a creation form has no write side effect. A protected foreground
    // read can disable its launcher between focus and activation; confirm the
    // form before continuing. Create/Save submissions still activate once.
    async function openCreation(name: string, fieldName: string) {
      const field = page.getByLabel(fieldName, { exact: false });
      await expect(async () => {
        if (await field.isVisible()) return;
        const button = page.getByRole("button", { name, exact: true });
        if (await button.isVisible() && await button.isEnabled({ timeout: 500 })) {
          if (viewport.name !== "desktop") {
            await button.focus({ timeout: 500 });
            await expect(button).toBeFocused({ timeout: 500 });
            await expect(button).toBeEnabled({ timeout: 500 });
            await button.press("Enter", { timeout: 500 });
          } else await button.click({ timeout: 500 });
        }
        await expect(field).toBeVisible({ timeout: 500 });
      }).toPass({ timeout: 5_000 });
    }
    async function expectViewportFit(view: Page = page) {
      await expect.poll(() => view.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      if (textZoom) expect(await view.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
    }
    async function expectEntryHeading(name: string) {
      if (!firstUseSignIn) return;
      await expect(page.getByTitle('Browser organization', { exact: true })).toBeVisible();
      await expect.poll(async () => {
        const heading = await page.getByRole('heading', { name, exact: true }).boundingBox();
        const header = await page.getByRole('banner').boundingBox();
        return !!heading && !!header && heading.y >= header.y + header.height
          && heading.y + heading.height <= viewport.height;
      }).toBe(true);
    }
    const email = `board-browser-${Date.now()}@example.test`;
    const password = "board-browser-correct-horse-battery";
    const headers = { "X-StrataAI-Request": "1" };
    const created = await registerVerifiedAccountFixture(context.request, { email, password, displayName: "Board browser" }, !!firstUseSignIn);
    if (firstUseSignIn) {
      await context.clearCookies();
      await page.goto('/login');
      await page.getByLabel(/^Email/).fill(email);
      await page.getByLabel(/^Password/).fill(password);
      const signIn = page.getByRole('button', { name: 'Sign in', exact: true });
      await focusAdmittedControl(signIn); await signIn.press('Enter');
      await expect(page).toHaveURL(/\/app$/);
    }
    await page.goto("/app");
    await expect(
      page.getByText("You have no organizations yet. Create one to begin."),
    ).toBeVisible();
    await expectViewportFit();
    await activate("Create organization");
    await page
      .getByLabel("Name", { exact: false })
      .fill("Browser organization");
    await page
      .getByLabel("Description", { exact: true })
      .fill("Board workflow");
    await activate("Create");
    if (firstUseSignIn) {
      await expect(page).toHaveURL(/\/app\/[0-9a-f-]+$/);
      restoreWorker = scopedBoardWorker(new URL(page.url()).pathname.split('/')[2]);
    }
    await expect(
      page.getByRole("heading", { name: "Browser organization", exact: true }),
    ).toBeVisible();
    await expectViewportFit();
    const organizationId = new URL(page.url()).pathname.split("/")[2];
    if (firstUseSignIn) {
      const canonical = await context.request.get(`/organizations/${organizationId}`);
      expect(canonical.status()).toBe(200);
      const summary = await canonical.json();
      expect(summary.role).toBe(0);
      expect(summary.organization).toMatchObject({ id: organizationId, name: 'Browser organization',
        description: 'Board workflow', logoUrl: null, ownerUserId: created.user.id, status: 0,
        type: 'STRATA', version: 1, createdAt: expect.any(String), updatedAt: expect.any(String) });
      expect(Number.isFinite(Date.parse(summary.organization.createdAt))).toBe(true);
      expect(Date.parse(summary.organization.updatedAt)).toBeGreaterThanOrEqual(Date.parse(summary.organization.createdAt));
      await expectEntryHeading('Browser organization');
      await page.screenshot({ path: info.outputPath('first-organization.png') });
    }
    // Live admission initially replaces the home controls while rechecking access.
    await expect(page.getByRole("status")).toHaveText("Current Board access checked.");
    await activate("Create board");
    await page.getByLabel("Name", { exact: false }).fill("Browser board");
    await page
      .getByLabel("Description", { exact: true })
      .fill("Persisted description");
    await activate("Create");
    await expect(
      page.getByRole("heading", { name: "Browser board", exact: true }),
    ).toBeVisible();
    await expectEntryHeading('Browser board');
    const path = new URL(page.url()).pathname;
    const boardId = path.split("/")[4];
    if (firstUseSignIn) await waitForBoardDelivery(context.request, boardId);
    const snapshot = await context.request.get(`/boards/${boardId}`);
    expect(snapshot.ok()).toBeTruthy();
    const board = (await snapshot.json()).board;
    await expect(
      page.getByRole("heading", { name: "Browser board", exact: true }),
    ).toBeVisible();
    await openCreation("Add list", "List name");
    await page.getByLabel("List name", { exact: false }).fill("Planning");
    await activate("Create");
    await expect(
      page.getByRole("heading", { name: "Planning", exact: true }),
    ).toBeVisible();
    await expectViewportFit();
    await openCreation("Add card to Planning", "Card title");
    await page.getByLabel("Card title", { exact: false }).fill("Inspect roof");
    // The server commits, but the first response is lost. An unchanged UI retry
    // must use the same intent key and recover exactly one persisted card.
    const retryKeys: (string | undefined)[] = [];
    const cardCreationUrl = (url: URL) => /^\/lists\/[^/]+\/cards$/.test(url.pathname);
    await page.route(cardCreationUrl, async (route) => {
      if (route.request().method() !== "POST") return route.continue();
      retryKeys.push(route.request().headers()["idempotency-key"]);
      const committed = await route.fetch();
      expect(committed.status()).toBe(201);
      if (retryKeys.length === 1) await route.abort("failed");
      else await route.fulfill({ response: committed });
    });
    await activate("Create");
    await expect(page.getByRole("alert")).toBeVisible();
    await expect(page.getByLabel("Card title", { exact: false })).toHaveValue(
      "Inspect roof",
    );
    await activate("Create");
    await expect(
      page.getByRole("link", { name: "Inspect roof", exact: true }),
    ).toBeVisible();
    expect(retryKeys).toHaveLength(2);
    expect(retryKeys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(retryKeys[1]).toBe(retryKeys[0]);
    await page.unroute(cardCreationUrl);
    const afterRetry = await (
      await context.request.get(`/boards/${boardId}`)
    ).json();
    expect(afterRetry.lists[0].cards).toHaveLength(1);
    await page.getByRole("link", { name: "Inspect roof", exact: true }).click();
    await expect(page.getByLabel("Card title", { exact: false })).toHaveValue(
      "Inspect roof",
    );
    await expectViewportFit();
    const cardPath = new URL(page.url()).pathname;
    const second = await context.newPage();
    await second.setViewportSize({ width: viewport.width, height: viewport.height });
    await second.goto(cardPath);
    await expect(second.getByLabel("Card title", { exact: false })).toHaveValue(
      "Inspect roof",
    );
    await second
      .getByLabel("Card title", { exact: false })
      .fill("Conflicting draft");
    await page
      .getByLabel("Card title", { exact: false })
      .fill("Inspect roof and gutters");
    await page
      .getByLabel("Description", { exact: true })
      .fill("Persistent edit");
    await activate("Save card");
    await expect(
      page.getByRole("status").filter({ hasText: "Changes saved." }),
    ).toHaveText("Changes saved.");
    if (firstUseSignIn) await page.screenshot({ path: info.outputPath('first-card-saved.png') });
    await second
      .getByRole("button", {
        name: "Refresh card",
        exact: true,
      })
      .click();
    await expect(second.getByRole("alert")).toContainText("changed elsewhere");
    await expect(second.getByLabel("Card title", { exact: false })).toHaveValue(
      "Conflicting draft",
    );
    await expect(
      second.getByRole("button", { name: "Save card", exact: true }),
    ).toBeDisabled();
    await second
      .getByRole("button", { name: "Discard edits and load latest card" })
      .click();
    await expect(second.getByLabel("Card title", { exact: false })).toHaveValue(
      "Inspect roof and gutters",
    );
    await second.reload();
    await expect(second.getByLabel("Description", { exact: true })).toHaveValue(
      "Persistent edit",
    );
    await second.getByRole("button", { name: "Close", exact: true }).click();
    await expect(second).toHaveURL(new RegExp(`${path}$`));
    await page.goBack();
    await expect(page).toHaveURL(new RegExp(`${path}$`));
    const returnCard = page.getByRole('link', { name: 'Inspect roof and gutters', exact: true });
    await expect(returnCard).toBeFocused();
    await page.goForward();
    await expect(page).toHaveURL(new RegExp(`${cardPath}$`));
    await expect(page.getByLabel('Card title', { exact: false })).toHaveValue('Inspect roof and gutters');
    await activate('Close');
    await expect(page).toHaveURL(new RegExp(`${path}$`));
    await expect(returnCard).toBeFocused();
    await page.goto(
      `/app/00000000-0000-0000-0000-000000000000/boards/${board.id}`,
    );
    await expect(page.getByRole("alert")).toContainText("unavailable");
    await expect(page.getByText("Browser board", { exact: true })).toHaveCount(
      0,
    );
    const publicResponse = await context.request.patch(
      `/boards/${board.id}/visibility`,
      { headers, data: { visibility: "PUBLIC", version: board.version } },
    );
    expect(publicResponse.ok()).toBeTruthy();
    const anonymous = await browser.newContext({ viewport: { width: viewport.width, height: viewport.height } });
    try {
      if (textZoom) await anonymous.addInitScript(applyTextZoom);
      const reader = await anonymous.newPage();
      await reader.goto(new URL(path, page.url()).toString());
      await expect(
        reader.getByRole("heading", { name: "Browser board", exact: true }),
      ).toBeVisible();
      await expectViewportFit(reader);
      await expect(
        reader.getByRole("button", { name: "Add list", exact: true }),
      ).toHaveCount(0);
      await reader
        .getByRole("link", { name: "Inspect roof and gutters", exact: true })
        .click();
      await expect(
        reader.getByLabel("Card title", { exact: false }),
      ).toBeDisabled();
      await expectViewportFit(reader);
      await expect(
        reader.getByRole("button", { name: "Save card", exact: true }),
      ).toHaveCount(0);
      if (firstUseSignIn) await reader.screenshot({ path: info.outputPath('public-card-readonly.png') });
    } finally {
      await anonymous.close();
    }
    } finally {
      restoreWorker?.();
    }
  });
}
