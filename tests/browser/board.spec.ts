import { expect, test } from "@playwright/test";

test("PRD-01/04/07/08/09: persisted board creation, deep links, conflict recovery and scope isolation", async ({
  page,
  context,
  browser,
}) => {
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
  const organizationResponse = await context.request.post("/organizations", {
    headers,
    data: { name: "Browser organization", description: "Board workflow" },
  });
  expect(organizationResponse.ok()).toBeTruthy();
  const organizationId = (await organizationResponse.json()).organization.id;
  const boardResponse = await context.request.post("/boards", {
    headers,
    data: {
      organizationId,
      name: "Browser board",
      description: "Persisted description",
      visibility: "PRIVATE",
      backgroundType: "COLOR",
      backgroundValue: "#0f4c81",
    },
  });
  expect(boardResponse.ok()).toBeTruthy();
  const board = await boardResponse.json();
  const path = `/app/${organizationId}/boards/${board.id}`;
  await page.goto(path);
  await expect(
    page.getByRole("heading", { name: "Browser board", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Add list", exact: true }).click();
  await page.getByLabel("List name", { exact: false }).fill("Planning");
  await page.getByRole("button", { name: "Create", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Planning", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Add card to Planning" }).click();
  await page.getByLabel("Card title", { exact: false }).fill("Inspect roof");
  await page.getByRole("button", { name: "Create", exact: true }).click();
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
  await page
    .getByLabel("Card title", { exact: false })
    .fill("Inspect roof and gutters");
  await page.getByLabel("Description", { exact: true }).fill("Persistent edit");
  await page.getByRole("button", { name: "Save card", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Changes saved.");
  await second
    .getByLabel("Card title", { exact: false })
    .fill("Conflicting draft");
  await second.getByRole("button", { name: "Save card", exact: true }).click();
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
  await expect(page.getByText("Browser board", { exact: true })).toHaveCount(0);
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
