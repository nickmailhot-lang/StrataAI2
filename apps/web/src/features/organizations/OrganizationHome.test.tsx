import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { OrganizationHome } from "./OrganizationHome";

const organizations = [
  {
    organization: {
      id: "org-1",
      name: "Council",
      description: "Organization description",
      status: 0,
    },
    role: 0,
  },
];
function response(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), { status });
}
function mount(path = "/app") {
  const router = createMemoryRouter(
    [
      { path: "/app", element: <OrganizationHome /> },
      { path: "/app/:organizationId", element: <OrganizationHome /> },
      {
        path: "/app/:organizationId/boards/:boardId",
        element: <h1>Created board destination</h1>,
      },
      { path: "/login", element: <h1>Sign in destination</h1> },
    ],
    { initialEntries: [path] },
  );
  render(<RouterProvider router={router} />);
  return router;
}
afterEach(() => vi.unstubAllGlobals());
describe("PRD-01/03/04 organization discovery", () => {
  it("displays an honest empty state and creation action for a new account", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response([])));
    mount();
    expect(
      await screen.findByText(/You have no organizations yet/),
    ).toBeVisible();
    expect(
      screen.getByRole("button", { name: "Create organization" }),
    ).toBeVisible();
    expect(screen.queryByText("demo")).not.toBeInTheDocument();
  });
  it("creates an organization and opens its authorized board list", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response(organizations[0], 201))
      .mockResolvedValueOnce(response(organizations))
      .mockResolvedValueOnce(response([]));
    vi.stubGlobal("fetch", fetcher);
    const router = mount();
    fireEvent.click(
      await screen.findByRole("button", { name: "Create organization" }),
    );
    fireEvent.change(screen.getByLabelText(/Name/), {
      target: { value: "Council" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^Create$/ }));
    expect(
      await screen.findByRole("heading", { name: "Council" }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe("/app/org-1");
    expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({
      name: "Council",
      description: "",
    });
    expect(fetcher.mock.calls[1][1].headers.get("X-StrataAI-Request")).toBe(
      "1",
    );
  });
  it("defaults new boards to private and opens the persisted board ID", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(organizations))
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response({ id: "new-board" }, 201));
    vi.stubGlobal("fetch", fetcher);
    const router = mount("/app/org-1");
    fireEvent.click(
      await screen.findByRole("button", { name: "Create board" }),
    );
    fireEvent.change(screen.getByLabelText(/Name/), {
      target: { value: "Planning" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^Create$/ }));
    await screen.findByText("Created board destination");
    expect(router.state.location.pathname).toBe("/app/org-1/boards/new-board");
    expect(JSON.parse(fetcher.mock.calls[2][1].body)).toMatchObject({
      name: "Planning",
      organizationId: "org-1",
      visibility: "PRIVATE",
    });
  });
  it("redirects expired sessions to sign in", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({}, 401)));
    mount();
    expect(await screen.findByText("Sign in destination")).toBeVisible();
  });
  it("does not request or expose boards for an organization outside active membership", async () => {
    const fetcher = vi.fn().mockResolvedValue(response(organizations));
    vi.stubGlobal("fetch", fetcher);
    mount("/app/another-org");
    expect(await screen.findByRole("alert")).toHaveTextContent("unavailable");
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("Council")).not.toBeInTheDocument();
  });
  it("clears organization names immediately when switching scope", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response(organizations))
      .mockResolvedValueOnce(
        response([
          {
            organization: { id: "org-2", name: "Other council", status: 0 },
            role: 2,
          },
        ]),
      )
      .mockResolvedValueOnce(
        response([{ id: "board-2", name: "Accessible board", version: 1 }]),
      );
    vi.stubGlobal("fetch", fetcher);
    const router = mount();
    await screen.findByText("Council");
    await router.navigate("/app/org-2");
    await waitFor(() =>
      expect(
        screen.getByRole("link", { name: "Accessible board" }),
      ).toHaveAttribute("href", "/app/org-2/boards/board-2"),
    );
    expect(screen.queryByText("Council")).not.toBeInTheDocument();
  });
});
