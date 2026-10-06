import { expect, test } from "./releaseTest";

for (const viewport of [
  { name: "desktop", width: 1280, height: 720 },
  { name: "mobile", width: 390, height: 844 },
]) {
  test(`PRD-01/04/07/08/09: persisted board workflow and isolation (${viewport.name})`, async ({
    page,
    context,
    browser,
  }) => {
    await page.setViewportSize({
      width: viewport.width,
      height: viewport.height,
    });
    async function activate(name: string) {
      const button = page.getByRole("button", { name, exact: true });
      if (viewport.name === "mobile") {
        await button.focus();
        await expect(button).toBeFocused();
        await button.press("Enter");
      } else await button.click();
    }
    const email = `board-browser-${Date.now()}@example.test`;
    const password = "board-browser-correct-horse-battery";
    const headers = { "X-StrataAI-Request": "1" };
    const registration = await context.request.post("/auth/register", {
      headers,
      data: { email, password, displayName: "Board browser" },
    });
    expect(registration.ok()).toBeTruthy();
    const login = await context.request.post("/auth/login", {
      headers,
      data: { email, password },
    });
    expect(login.ok()).toBeTruthy();
    await page.goto("/app");
    await expect(
      page.getByText("You have no organizations yet. Create one to begin."),
    ).toBeVisible();
    await activate("Create organization");
    await page
      .getByLabel("Name", { exact: false })
      .fill("Browser organization");
    await page
      .getByLabel("Description", { exact: true })
      .fill("Board workflow");
    await activate("Create");
    await expect(
      page.getByRole("heading", { name: "Browser organization", exact: true }),
    ).toBeVisible();
    const organizationId = new URL(page.url()).pathname.split("/")[2];
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
    const path = new URL(page.url()).pathname;
    const boardId = path.split("/")[4];
    const snapshot = await context.request.get(`/boards/${boardId}`);
    expect(snapshot.ok()).toBeTruthy();
    const board = (await snapshot.json()).board;
    await expect(
      page.getByRole("heading", { name: "Browser board", exact: true }),
    ).toBeVisible();
    await activate("Add list");
    await page.getByLabel("List name", { exact: false }).fill("Planning");
    await activate("Create");
    await expect(
      page.getByRole("heading", { name: "Planning", exact: true }),
    ).toBeVisible();
    await activate("Add card to Planning");
    await page.getByLabel("Card title", { exact: false }).fill("Inspect roof");
    // The server commits, but the first response is lost. An unchanged UI retry
    // must use the same intent key and recover exactly one persisted card.
    const retryKeys: (string | undefined)[] = [];
    await page.route("**/lists/*/cards", async (route) => {
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
    await page.unroute("**/lists/*/cards");
    const afterRetry = await (
      await context.request.get(`/boards/${boardId}`)
    ).json();
    expect(afterRetry.lists[0].cards).toHaveLength(1);
    await page.getByRole("link", { name: "Inspect roof", exact: true }).click();
    await expect(page.getByLabel("Card title", { exact: false })).toHaveValue(
      "Inspect roof",
    );
    const cardPath = new URL(page.url()).pathname;
    const second = await context.newPage();
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
    const anonymous = await browser.newContext();
    try {
      const reader = await anonymous.newPage();
      await reader.goto(new URL(path, page.url()).toString());
      await expect(
        reader.getByRole("heading", { name: "Browser board", exact: true }),
      ).toBeVisible();
      await expect(
        reader.getByRole("button", { name: "Add list", exact: true }),
      ).toHaveCount(0);
      await reader
        .getByRole("link", { name: "Inspect roof and gutters", exact: true })
        .click();
      await expect(
        reader.getByLabel("Card title", { exact: false }),
      ).toBeDisabled();
      await expect(
        reader.getByRole("button", { name: "Save card", exact: true }),
      ).toHaveCount(0);
    } finally {
      await anonymous.close();
    }
  });
}
