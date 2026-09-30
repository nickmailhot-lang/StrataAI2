import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CardDetailEditor } from "./CardDetailEditor";
import { WorkRequestError, type WorkCard } from "../../api/workManagement";

const card: WorkCard = {
  id: "one",
  title: "Original",
  description: "Original notes",
  rank: "a",
  version: 3,
};
function props() {
  return {
    card,
    editable: true,
    busy: false,
    saved: false,
    renderError: (error: Error) => <div role="alert">{error.message}</div>,
    onSubmit: vi
      .fn<
        (
          event: React.FormEvent<HTMLFormElement>,
          expectedVersion: number,
        ) => void
      >()
      .mockImplementation((event) => event.preventDefault()),
    onDiscard: vi.fn().mockResolvedValue(undefined),
    onRefresh: vi.fn(),
  };
}
describe("PRD-09/22 drafts across incoming versions", () => {
  it("preserves dirty text, focus and baseline on refresh, and blocks an unsafe save", () => {
    const initial = props();
    const { rerender } = render(<CardDetailEditor {...initial} />);
    const title = screen.getByLabelText(/Card title/);
    fireEvent.change(title, { target: { value: "My draft" } });
    title.focus();
    rerender(
      <CardDetailEditor
        {...initial}
        card={{ ...card, title: "Remote", version: 4 }}
      />,
    );
    expect(title).toHaveValue("My draft");
    expect(title).toHaveFocus();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Your draft is preserved",
    );
    expect(screen.getByRole("button", { name: "Save card" })).toBeDisabled();
    fireEvent.submit((title as HTMLInputElement).form!);
    expect(initial.onSubmit).not.toHaveBeenCalled();
  });
  it("adopts a new version in a clean editor and submits that baseline", () => {
    const initial = props();
    const { rerender } = render(<CardDetailEditor {...initial} />);
    rerender(
      <CardDetailEditor
        {...initial}
        card={{ ...card, title: "Remote", version: 4 }}
      />,
    );
    expect(screen.getByLabelText(/Card title/)).toHaveValue("Remote");
    fireEvent.click(screen.getByRole("button", { name: "Save card" }));
    expect(initial.onSubmit.mock.calls[0][1]).toBe(4);
  });
  it("accepts a successful save before its snapshot without rolling it back", () => {
    const initial = props();
    const { rerender } = render(<CardDetailEditor {...initial} />);
    fireEvent.change(screen.getByLabelText(/Card title/), {
      target: { value: "My draft" },
    });
    const acknowledged = { ...card, title: "Saved draft", version: 4 };
    rerender(
      <CardDetailEditor {...initial} acknowledged={acknowledged} saved />,
    );
    expect(screen.getByLabelText(/Card title/)).toHaveValue("Saved draft");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Changes saved");
    rerender(
      <CardDetailEditor
        {...initial}
        acknowledged={acknowledged}
        card={{ ...card }}
        saved
      />,
    );
    expect(screen.getByLabelText(/Card title/)).toHaveValue("Saved draft");
  });
  it("retains a draft when explicit conflict recovery fails and replaces it only after success", async () => {
    const initial = props();
    initial.onDiscard
      .mockRejectedValueOnce(new Error("Unavailable"))
      .mockResolvedValueOnce({ ...card, title: "Latest", version: 5 });
    render(
      <CardDetailEditor {...initial} error={new WorkRequestError(409, null)} />,
    );
    fireEvent.change(screen.getByLabelText(/Card title/), {
      target: { value: "My draft" },
    });
    fireEvent.click(
      screen.getByRole("button", {
        name: "Discard edits and load latest card",
      }),
    );
    await waitFor(() => expect(initial.onDiscard).toHaveBeenCalledTimes(1));
    expect(screen.getByLabelText(/Card title/)).toHaveValue("My draft");
    fireEvent.click(
      screen.getByRole("button", {
        name: "Discard edits and load latest card",
      }),
    );
    await waitFor(() =>
      expect(screen.getByLabelText(/Card title/)).toHaveValue("Latest"),
    );
  });
  it("removes save controls after edit access is revoked and disables the retained fields", () => {
    const initial = props();
    const { rerender } = render(<CardDetailEditor {...initial} />);
    fireEvent.change(screen.getByLabelText(/Card title/), {
      target: { value: "My draft" },
    });
    rerender(<CardDetailEditor {...initial} editable={false} />);
    expect(screen.getByLabelText(/Card title/)).toHaveValue("My draft");
    expect(screen.getByLabelText(/Card title/)).toBeDisabled();
    expect(
      screen.queryByRole("button", { name: "Save card" }),
    ).not.toBeInTheDocument();
  });
});
