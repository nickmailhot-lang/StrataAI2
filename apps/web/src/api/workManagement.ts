import { apiFetch } from "./apiFetch";
export type WorkCard = {
  id: string;
  title: string;
  description: string | null;
  rank: string;
  version: number;
};
export type BoardSnapshot = {
  board: {
    id: string;
    organizationId: string;
    name: string;
    description: string | null;
    lifecycleState: string;
  };
  lists: {
    list: { id: string; name: string; rank: string; lifecycleState: string };
    cards: WorkCard[];
  }[];
  access: {
    canView: boolean;
    canEdit: boolean;
    canMove: boolean;
    canAdminister: boolean;
  };
};
export class WorkRequestError extends Error {
  constructor(
    public status: number,
    public correlationId: string | null,
  ) {
    super(
      status === 409
        ? "This item changed elsewhere. Refresh the board before trying again."
        : status === 401
          ? "Sign in to continue."
          : status === 403 || status === 404
            ? "This board or action is unavailable."
            : status === 400
              ? "Check the fields and try again."
              : "Unable to complete the request. Please try again.",
    );
  }
}
// Only local, fixed validation messages use this type; never API response text.
export class WorkInputError extends Error {}
export async function workRequest<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await apiFetch(path, options);
  if (!response.ok)
    throw new WorkRequestError(
      response.status,
      response.headers.get("X-Correlation-ID"),
    );
  return response.status === 204
    ? (undefined as T)
    : ((await response.json()) as T);
}
export async function loadBoard(
  organizationId: string,
  boardId: string,
  signal: AbortSignal,
) {
  const data = await workRequest<BoardSnapshot>(
    `/boards/${encodeURIComponent(boardId)}`,
    { signal },
  );
  if (
    data.board.id !== boardId ||
    data.board.organizationId !== organizationId ||
    !data.access.canView
  )
    throw new WorkRequestError(404, null);
  return data;
}
export function mutateWork(path: string, method: string, body: unknown) {
  return workRequest<unknown>(path, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}
