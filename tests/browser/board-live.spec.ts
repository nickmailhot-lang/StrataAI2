import { expect, test, type WebSocketRoute } from "./releaseTest";
import { execFileSync } from "node:child_process";

test("PRD-22: desktop and phone boards consume live changes, preserve drafts, recover and clear revoked scope", async ({
  page,
  context,
  browser,
}) => {
  test.setTimeout(180_000);
  const headers = { "X-StrataAI-Request": "1" };
  const email = `board-live-${Date.now()}@example.test`,
    password = "live-board-correct-horse";
  expect(
    (
      await context.request.post("/auth/register", {
        headers,
        data: { email, password, displayName: "Live board fixture" },
      })
    ).ok(),
  ).toBeTruthy();
  expect(
    (
      await context.request.post("/auth/login", {
        headers,
        data: { email, password },
      })
    ).ok(),
  ).toBeTruthy();
  const organization = (
    await (
      await context.request.post("/organizations", {
        headers,
        data: { name: "Live board fixture" },
      })
    ).json()
  ).organization.id;
  const board = (
    await (
      await context.request.post("/boards", {
        headers,
        data: { organizationId: organization, name: "Live board" },
      })
    ).json()
  ).id;
  const list = (
    await (
      await context.request.post(`/boards/${board}/lists`, {
        headers,
        data: { name: "Live list" },
      })
    ).json()
  ).id;
  const card = (
    await (
      await context.request.post(`/lists/${list}/cards`, {
        headers,
        data: { title: "Initial live card" },
      })
    ).json()
  ).id;
  const phone = await browser.newContext({
    viewport: { width: 390, height: 844 },
    storageState: await context.storageState(),
  });
  const other = await phone.newPage();
  let unavailable = false;
  let socket: WebSocketRoute | undefined;
  await phone.routeWebSocket("**/boards/live*", (route) => {
    if (unavailable) {
      route.close({ code: 1013 });
      return;
    }
    socket = route;
    route.connectToServer();
  });
  const files = [
    "-f",
    "compose.release.yml",
    "-f",
    "scripts/ci/compose.identity-test.yml",
  ];
  const path = `/app/${organization}/boards/${board}`;
  try {
    if (process.env.CI === "true")
      execFileSync(
        "docker",
        [
          "compose",
          ...files,
          "-f",
          "scripts/ci/compose.work-event-test.yml",
          "up",
          "-d",
          "--force-recreate",
          "--wait",
          "--wait-timeout",
          "180",
          "worker",
        ],
        {
          env: {
            ...process.env,
            STRATAAI_TEST_EVENT_ORGANIZATION_ID: organization,
          },
          stdio: "pipe",
        },
      );
    await page.goto(path);
    await other.goto(path);
    for (const target of [page, other])
      await expect(
        target.getByText("Live updates connected.", { exact: true }),
      ).toBeVisible({ timeout: 30_000 });
    expect(
      (
        await context.request.patch(`/cards/${card}`, {
          headers,
          data: { title: "Pushed card title", version: 1 },
        })
      ).ok(),
    ).toBeTruthy();
    for (const target of [page, other])
      await expect(
        target.getByRole("link", { name: "Pushed card title", exact: true }),
      ).toBeVisible({ timeout: 15_000 });
    await other
      .getByRole("link", { name: "Pushed card title", exact: true })
      .focus();
    await other.keyboard.press("Enter");
    const title = other.getByRole("textbox", { name: /Card title/ });
    await title.fill("My protected unsaved draft");
    await title.focus();
    expect(
      (
        await context.request.patch(`/cards/${card}`, {
          headers,
          data: { title: "Another client edit", version: 2 },
        })
      ).ok(),
    ).toBeTruthy();
    await expect(other.getByText(/This card changed elsewhere/)).toBeVisible({
      timeout: 15_000,
    });
    await expect(title).toHaveValue("My protected unsaved draft");
    await expect(title).toBeFocused();
    await expect(
      other.getByRole("button", { name: "Save card", exact: true }),
    ).toBeDisabled();
    await other
      .getByRole("button", { name: "Discard edits and load latest card" })
      .click();
    await expect(title).toHaveValue("Another client edit");
    // Hold one real browser GET beyond the read deadline. A later event queues
    // recovery, while its dirty draft must remain pinned to the previous version.
    await title.fill("Draft through a hung refresh");
    await title.focus();
    let heldRead = false,
      readAborted = false;
    other.on("requestfailed", (request) => {
      if (
        new URL(request.url()).pathname === `/boards/${board}` &&
        request.failure()?.errorText.includes("ERR_ABORTED")
      )
        readAborted = true;
    });
    await other.route(`**/boards/${board}`, async (route) => {
      if (heldRead) {
        await route.continue();
        return;
      }
      heldRead = true;
      await new Promise((resolve) => setTimeout(resolve, 20_000));
      await route.abort().catch(() => {});
    });
    await other
      .getByRole("button", { name: "Refresh card", exact: true })
      .click();
    expect(
      (
        await context.request.patch(`/cards/${card}`, {
          headers,
          data: { title: "Recovered after read timeout", version: 3 },
        })
      ).ok(),
    ).toBeTruthy();
    await expect.poll(() => readAborted, { timeout: 25_000 }).toBe(true);
    await expect(other.getByText(/This card changed elsewhere/)).toBeVisible({
      timeout: 15_000,
    });
    await expect(title).toHaveValue("Draft through a hung refresh");
    await other.unroute(`**/boards/${board}`);
    await other
      .getByRole("button", { name: "Discard edits and load latest card" })
      .click();
    await expect(title).toHaveValue("Recovered after read timeout");
    unavailable = true;
    await socket!.close({ code: 1012 });
    expect(
      (
        await context.request.patch(`/cards/${card}`, {
          headers,
          data: { title: "Recovered during outage", version: 4 },
        })
      ).ok(),
    ).toBeTruthy();
    await expect(title).toHaveValue("Recovered during outage", {
      timeout: 20_000,
    });
    unavailable = false;
    await expect(
      other.getByText("Live updates connected.", { exact: true }),
    ).toBeVisible({ timeout: 45_000 });
    expect(
      (
        await context.request.patch(`/cards/${card}`, {
          headers,
          data: { title: "Reconnected pushed title", version: 5 },
        })
      ).ok(),
    ).toBeTruthy();
    await expect(title).toHaveValue("Reconnected pushed title", {
      timeout: 15_000,
    });
    expect(
      (
        await context.request.post("/auth/logout", { headers, data: {} })
      ).status(),
    ).toBe(204);
    for (const target of [page, other]) {
      await expect(
        target.getByRole("heading", { name: "Live board", exact: true }),
      ).toHaveCount(0, { timeout: 10_000 });
      await expect(
        target.getByRole("textbox", { name: /Card title/ }),
      ).toHaveCount(0);
    }
  } finally {
    if (process.env.CI === "true")
      execFileSync(
        "docker",
        [
          "compose",
          ...files,
          "up",
          "-d",
          "--force-recreate",
          "--wait",
          "--wait-timeout",
          "180",
          "worker",
        ],
        { stdio: "pipe" },
      );
    await phone.close();
  }
});
