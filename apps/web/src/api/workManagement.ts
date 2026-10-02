import { apiFetch } from "./apiFetch";
export type WorkCard = {
  startAt?: string | null;
  dueAt?: string | null;
  dueTimezone?: string | null;
  dueHasTime?: boolean;
  dueComplete?: boolean;
  id: string;
  title: string;
  description: string | null;
  rank: string;
  version: number;
};
export type BoardSnapshot = {
  cardMembers?: Record<string, { items: { userId: string; displayName: string }[]; total: number; cardVersion: number }> | null;
  cardLabels?: Record<string, { items: { id: string; name: string; color: string }[]; total: number }>;
  board: {
    id: string;
    organizationId: string;
    name: string;
    description: string | null;
    lifecycleState: string;
  };
  lists: {
    list: { id: string; name: string; rank: string; lifecycleState: string; version?: number };
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
    public code?: string,
  ) {
    super(
      status === 409 && code === "idempotency_key_expired"
        ? "This submission expired. Reload to check the latest state before starting another change."
        : status === 409 && code === "idempotency_key_reused"
          ? "This submission cannot be reused for different input. Reload to check the latest state."
          : status === 409
            ? "This item changed elsewhere. Refresh the board before trying again."
            : status === 401
              ? "Sign in to continue."
              : status === 403 || status === 404
                ? "This board or action is unavailable."
                : status === 400
                  ? "Check the fields and try again."
                  : status === 503
                    ? "Service temporarily unavailable. Reload to check the latest state before retrying."
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
  options.signal?.throwIfAborted();
  const response = await apiFetch(path, options);
  options.signal?.throwIfAborted();
  if (!response.ok) {
    // Use only known stable codes; never display server titles/details or SQL.
    let code: string | undefined;
    try {
      const problem: unknown = await response.json();
      if (
        problem &&
        typeof problem === "object" &&
        "code" in problem &&
        (problem.code === "idempotency_key_expired" ||
          problem.code === "idempotency_key_reused")
      )
        code = problem.code;
    } catch {
      /* An edge/proxy error may have no JSON problem response. */
    }
    throw new WorkRequestError(
      response.status,
      response.headers.get("X-Correlation-ID"),
      code,
    );
  }
  const result = response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  options.signal?.throwIfAborted();
  return result;
}
export async function loadBoard(
  organizationId: string,
  boardId: string,
  signal: AbortSignal,
) {
  const data = await boundedWorkRead(
    (bounded) =>
      workRequest<BoardSnapshot>(`/boards/${encodeURIComponent(boardId)}`, {
        signal: bounded,
      }),
    signal,
  );
  if (
    data.board.id !== boardId ||
    data.board.organizationId !== organizationId ||
    !data.access.canView
  )
    throw new WorkRequestError(404, null);
  return data;
}
export async function boundedWorkRead<T>(
  read: (signal: AbortSignal) => Promise<T>,
  signal: AbortSignal,
): Promise<T> {
  if (signal.aborted)
    throw new DOMException("Board read cancelled.", "AbortError");
  const controller = new AbortController();
  let timeout: ReturnType<typeof setTimeout> | undefined;
  let cancel = () => {};
  const stopped = new Promise<never>((_, reject) => {
    cancel = () => {
      reject(new DOMException("Board read cancelled.", "AbortError"));
      controller.abort();
    };
    signal.addEventListener("abort", cancel, { once: true });
    timeout = setTimeout(() => {
      reject(new WorkRequestError(503, null));
      controller.abort();
    }, 15_000);
  });
  try {
    return await Promise.race([
      Promise.resolve().then(() => read(controller.signal)),
      stopped,
    ]);
  } finally {
    clearTimeout(timeout);
    signal.removeEventListener("abort", cancel);
  }
}
export function mutateWork<T = unknown>(
  path: string,
  method: string,
  body: unknown,
  retryKey: string = crypto.randomUUID(),
) {
  return workRequest<T>(path, {
    method,
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": retryKey,
    },
    body: JSON.stringify(body),
  });
}

// Keep the key while an unchanged intent has an uncertain outcome. Changed
// input, another resource, or a completed operation starts a new intent.
export class WorkMutationIntent {
  private pending?: { signature: string; key: string };
  async send<T = unknown>(
    path: string,
    method: string,
    body: unknown,
  ): Promise<T> {
    const signature = JSON.stringify({ path, method, body });
    if (this.pending?.signature !== signature)
      this.pending = { signature, key: crypto.randomUUID() };
    const intent = this.pending;
    try {
      const result = await mutateWork<T>(path, method, body, intent.key);
      if (this.pending === intent) this.pending = undefined;
      return result;
    } catch (reason) {
      const failure =
        reason instanceof WorkRequestError
          ? reason
          : new WorkRequestError(0, null);
      if (failure.status === 0 || failure.status >= 500)
        failure.message =
          "Unable to confirm the save. Keep these fields unchanged and try again to recover this submission.";
      throw failure;
    }
  }
}
