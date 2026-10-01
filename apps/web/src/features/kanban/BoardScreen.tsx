import { useEffect, useEffectEvent, useRef, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import {
  loadBoard,
  WorkMutationIntent,
  WorkRequestError,
  WorkInputError,
  type BoardSnapshot,
  type WorkCard,
} from "../../api/workManagement";
import { CardDetailEditor } from "./CardDetailEditor";
import { CardMoveControls } from "./CardMoveControls";
import { ListPositionControls } from "./ListPositionControls";
import { previewCardMove, type CardMovePreview } from "./cardMovePreview";
import { watchBoard, type LiveStatus } from "../../api/boardLive";
type Loaded = { key: string; snapshot?: BoardSnapshot; error?: Error };
type Creation = { kind: "list" | "card"; listId?: string };
type AcknowledgedCard = { listId: string; card: WorkCard };
function preserveAcknowledged(
  snapshot: BoardSnapshot,
  acknowledged?: AcknowledgedCard,
): BoardSnapshot {
  if (!acknowledged) return snapshot;
  return {
    ...snapshot,
    lists: snapshot.lists.map((column) =>
      column.list.id !== acknowledged.listId ||
      column.list.lifecycleState !== "active"
        ? column
        : {
            ...column,
            cards: column.cards.map((card) =>
              card.id === acknowledged.card.id &&
              card.version < acknowledged.card.version
                ? acknowledged.card
                : card,
            ),
          },
    ),
  };
}
// PRD-01/04/07/08/09: scoped authoritative data and persisted creation/edits.
export function BoardScreen() {
  const { organizationId, boardId } = useParams();
  return <BoardContent key={`${organizationId}/${boardId}`} />;
}
function BoardContent() {
  const { organizationId = "", boardId = "", cardId } = useParams();
  const key = `${organizationId}/${boardId}`;
  const [loaded, setLoaded] = useState<Loaded>();
  const [reload, setReload] = useState(0);
  const [creation, setCreation] = useState<Creation>();
  const [busy, setBusy] = useState(false);
  const [movePreview, setMovePreview] = useState<CardMovePreview>();
  const mutation = useRef(new WorkMutationIntent());
  const activeRead = useRef<AbortController | undefined>(undefined);
  const reading = useRef(false);
  const [snapshotReading, setSnapshotReading] = useState(true);
  const cardLinks = useRef(new Map<string, HTMLAnchorElement>());
  const closeFocusCard = useRef<string | undefined>(undefined);
  const boardRefresh = useRef<HTMLButtonElement>(null);
  const queuedRefresh = useRef(false);
  const [liveStatus, setLiveStatus] = useState<LiveStatus>("connecting");
  const accessEpoch = useRef(0);
  const [failure, setFailure] = useState<{ cardId?: string; error: Error }>();
  const [acknowledged, setAcknowledged] = useState<WorkCard>();
  const acknowledgedCard = useRef<AcknowledgedCard | undefined>(undefined);
  const [savedFor, setSavedFor] = useState<{ cardId?: string }>();
  const error = failure?.cardId === cardId ? failure?.error : undefined;
  const saved = Boolean(savedFor) && savedFor?.cardId === cardId;
  function setError(next?: Error) {
    setFailure(next ? { cardId, error: next } : undefined);
  }
  function setSaved(next: boolean) {
    setSavedFor(next ? { cardId } : undefined);
  }
  function clearDeniedScope(failure: Error) {
    accessEpoch.current += 1;
    activeRead.current?.abort();
    queuedRefresh.current = false;
    mutation.current = new WorkMutationIntent();
    setAcknowledged(undefined);
    acknowledgedCard.current = undefined;
    setSavedFor(undefined);
    setCreation(undefined);
    setFailure(undefined);
    setLoaded({ key, error: failure });
  }
  const navigate = useNavigate();
  const location = useLocation();
  function finishRead(controller: AbortController) {
    if (activeRead.current !== controller) return;
    reading.current = false;
    setSnapshotReading(false);
    if (queuedRefresh.current && !controller.signal.aborted) {
      queuedRefresh.current = false;
      setReload((value) => value + 1);
    }
  }
  useEffect(() => () => activeRead.current?.abort(), []);
  useEffect(() => {
    activeRead.current?.abort();
    const controller = new AbortController();
    activeRead.current = controller;
    reading.current = true;
    setSnapshotReading(true);
    void loadBoard(organizationId, boardId, controller.signal)
      .then((snapshot) => {
        if (!controller.signal.aborted)
          setLoaded({
            key,
            snapshot: preserveAcknowledged(snapshot, acknowledgedCard.current),
          });
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return;
        const failure =
          reason instanceof Error
            ? reason
            : new Error("Unable to load this board.");
        const denied =
          failure instanceof WorkRequestError &&
          [401, 403, 404].includes(failure.status);
        if (denied) {
          clearDeniedScope(failure);
          return;
        }
        setLoaded((previous) => ({
          key,
          error: failure,
          snapshot:
            !denied && previous?.key === key ? previous.snapshot : undefined,
        }));
      })
      .finally(() => finishRead(controller));
    return () => controller.abort();
  }, [organizationId, boardId, key, reload]);
  const snapshot = loaded?.key === key ? loaded.snapshot : undefined;
  const loadError = loaded?.key === key ? loaded.error : undefined;
  const subscribed = Boolean(snapshot);
  const invalidate = useEffectEvent(() => {
    if (reading.current) queuedRefresh.current = true;
    else setReload((value) => value + 1);
  });
  useEffect(() => {
    if (!subscribed) return;
    return watchBoard({
      organizationId,
      boardId,
      invalidate: () => invalidate(),
      status: setLiveStatus,
    });
  }, [organizationId, boardId, subscribed]);
  const retrySnapshot = Boolean(snapshot && loadError);
  useEffect(() => {
    if (!retrySnapshot) return;
    const retry = setTimeout(() => invalidate(), 10_000);
    return () => clearTimeout(retry);
  }, [retrySnapshot, reload]);
  const card = snapshot?.lists
    .flatMap((column) => column.cards)
    .find((item) => item.id === cardId);
  const editable =
    snapshot?.access.canEdit && snapshot.board.lifecycleState === "active";
  const boardPath = `/app/${organizationId}/boards/${boardId}`;
  function closeCard() {
    closeFocusCard.current = cardId;
    if (location.state?.cardOverlay) navigate(-1);
    else navigate(boardPath, { replace: true });
  }
  async function submit(
    event: React.FormEvent<HTMLFormElement>,
    edit = false,
    expectedVersion?: number,
  ) {
    event.preventDefault();
    if (busy || !editable) return;
    const form = new FormData(event.currentTarget);
    const title = String(form.get("title") ?? "").trim();
    if (!title) {
      setError(
        new WorkInputError(
          creation?.kind === "list"
            ? "Enter a list name."
            : "Enter a card title.",
        ),
      );
      return;
    }
    setBusy(true);
    const epoch = accessEpoch.current;
    setError(undefined);
    setSaved(false);
    try {
      if (edit && card) {
        const updated = await mutation.current.send<WorkCard>(
          `/cards/${card.id}`,
          "PATCH",
          {
            title,
            description: String(form.get("description") ?? ""),
            version: expectedVersion ?? card.version,
          },
        );
        if (epoch !== accessEpoch.current) return;
        if (
          updated.id !== card.id ||
          !Number.isSafeInteger(updated.version) ||
          updated.version <= (expectedVersion ?? card.version) ||
          typeof updated.title !== "string" ||
          typeof updated.rank !== "string" ||
          (updated.description !== null &&
            typeof updated.description !== "string")
        )
          throw new WorkRequestError(503, null);
        const listId = snapshot?.lists.find((column) =>
          column.cards.some((item) => item.id === card.id),
        )?.list.id;
        if (listId) {
          acknowledgedCard.current = { listId, card: updated };
          activeRead.current?.abort();
          queuedRefresh.current = false;
          setLoaded((previous) =>
            previous?.key === key && previous.snapshot
              ? {
                  ...previous,
                  snapshot: preserveAcknowledged(
                    previous.snapshot,
                    acknowledgedCard.current,
                  ),
                }
              : previous,
          );
        }
        setAcknowledged(updated);
      } else if (creation?.kind === "list")
        await mutation.current.send(`/boards/${boardId}/lists`, "POST", {
          name: title,
        });
      else if (creation?.listId)
        await mutation.current.send(`/lists/${creation.listId}/cards`, "POST", {
          title,
        });
      if (epoch !== accessEpoch.current) return;
      setCreation(undefined);
      setSaved(true);
      setReload((value) => value + 1);
    } catch (reason) {
      if (epoch !== accessEpoch.current) return;
      if (
        reason instanceof WorkRequestError &&
        [401, 403, 404].includes(reason.status)
      ) {
        clearDeniedScope(reason);
      }
      setError(
        reason instanceof WorkRequestError
          ? reason
          : new Error("Unable to save."),
      );
    } finally {
      setBusy(false);
    }
  }
  async function discardAndLoad(): Promise<WorkCard | undefined> {
    setBusy(true);
    activeRead.current?.abort();
    const controller = new AbortController();
    activeRead.current = controller;
    reading.current = true;
    try {
      const latest = await loadBoard(
        organizationId,
        boardId,
        controller.signal,
      );
      if (controller.signal.aborted) return undefined;
      const reconciled = preserveAcknowledged(latest, acknowledgedCard.current);
      setLoaded({ key, snapshot: reconciled });
      setError(undefined);
      setSaved(false);
      return reconciled.lists
        .flatMap((column) => column.cards)
        .find((item) => item.id === cardId);
    } catch (reason) {
      if (controller.signal.aborted) return undefined;
      const failure =
        reason instanceof Error
          ? reason
          : new Error("Unable to load this board.");
      setError(failure);
      if (
        failure instanceof WorkRequestError &&
        [401, 403, 404].includes(failure.status)
      )
        clearDeniedScope(failure);
      throw failure;
    } finally {
      finishRead(controller);
      setBusy(false);
    }
  }
  const message = (failure: Error) => (
    <Alert severity="error">
      {failure instanceof WorkRequestError || failure instanceof WorkInputError
        ? failure.message
        : "Unable to load or save this board. Please try again."}
      {failure instanceof WorkRequestError &&
        failure.correlationId &&
        ` Reference: ${failure.correlationId}`}
    </Alert>
  );
  if (loadError && !snapshot)
    return (
      <Stack spacing={2}>
        {message(loadError)}
        <Button onClick={() => setReload((value) => value + 1)}>Retry</Button>
        {loadError instanceof WorkRequestError && loadError.status === 401 && (
          <Button component={Link} to="/login">
            Sign in
          </Button>
        )}
      </Stack>
    );
  if (!snapshot) return <CircularProgress aria-label="Loading board" />;
  return (
    <Stack spacing={2}>
      {loadError && message(loadError)}
      <Typography role="status" aria-live="polite" variant="body2">
        {liveStatus === "live"
          ? "Live updates connected."
          : liveStatus === "connecting"
            ? "Connecting live updates."
            : liveStatus === "recovering"
              ? "Recovering board updates."
              : "Live updates unavailable. Checking for changes automatically."}
      </Typography>
      <Stack
        direction="row"
        sx={{ justifyContent: "space-between", flexWrap: "wrap", gap: 1 }}
      >
        <Box>
          <Typography variant="h4" component="h2">
            {snapshot.board.name}
          </Typography>
          <Typography>{snapshot.board.description}</Typography>
        </Box>
        <Stack direction="row" sx={{ flexWrap: "wrap", gap: 1 }}>
          <Button
            ref={boardRefresh}
            disabled={busy}
            onClick={() => setReload((value) => value + 1)}
          >
            Refresh board
          </Button>
          {snapshot.access.canAdminister && snapshot.board.lifecycleState === "active" && (
            <><Button component={Link} to={`/app/${organizationId}/boards/${boardId}/invite`}>Invite to Board</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/visibility`}>Board visibility</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/members`}>Board members</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/invitations`}>Board invitations</Button></>
          )}
          {editable && (
            <Button
              onClick={() => {
                setError(undefined);
                setCreation({ kind: "list" });
              }}
            >
              Add list
            </Button>
          )}
        </Stack>
      </Stack>
      {saved && <Typography role="status">Changes saved.</Typography>}
      {snapshot.board.lifecycleState !== "active" && (
        <Alert severity="info">
          This board is archived. Editing is unavailable.
        </Alert>
      )}
      {snapshot.lists.length === 0 && (
        <Typography>
          No lists yet.{editable && " Add a list to begin."}
        </Typography>
      )}
      <Box
        aria-label="Kanban board"
        sx={{
          display: "grid",
          gridAutoFlow: "column",
          gridAutoColumns: { xs: "82vw", sm: 320 },
          gap: 2,
          overflowX: "auto",
          pb: 2,
        }}
      >
        {previewCardMove(snapshot, movePreview).lists.map((column) => (
          <Box
            key={column.list.id}
            component="section"
            aria-labelledby={`list-name-${column.list.id}`}
            sx={{ bgcolor: "grey.100", borderRadius: 2, p: 2, minHeight: 240 }}
          >
            <Typography id={`list-name-${column.list.id}`} variant="h6" component="h3">
              {column.list.name}
            </Typography>
            {snapshot.access.canMove && snapshot.board.lifecycleState === "active" && column.list.lifecycleState === "active" && <ListPositionControls
              list={column.list} snapshot={snapshot} disabled={busy || snapshotReading || !!loadError} onBusyChange={setBusy}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}
            <Stack spacing={1} sx={{ mt: 2 }}>
              {column.cards.map((item) => (
                <Card
                  key={item.id}
                  component={Link}
                  ref={(node: HTMLAnchorElement | null) => {
                    if (node) cardLinks.current.set(item.id, node);
                    else cardLinks.current.delete(item.id);
                  }}
                  to={`${boardPath}/cards/${item.id}`}
                  state={{ cardOverlay: true }}
                  sx={{
                    color: "inherit",
                    textDecoration: "none",
                    "&:focus-visible": {
                      outline: "3px solid",
                      outlineColor: "primary.main",
                    },
                  }}
                >
                  <CardContent>{item.title}</CardContent>
                </Card>
              ))}
            </Stack>
            {column.cards.length === 0 && (
              <Typography sx={{ my: 2 }}>No cards yet.</Typography>
            )}
            {editable && column.list.lifecycleState === "active" && (
              <Button
                onClick={() => {
                  setError(undefined);
                  setCreation({ kind: "card", listId: column.list.id });
                }}
              >
                Add card to {column.list.name}
              </Button>
            )}
          </Box>
        ))}
      </Box>
      <Dialog
        open={Boolean(creation)}
        onClose={() => {
          if (!busy) setCreation(undefined);
        }}
        fullWidth
        maxWidth="sm"
      >
        <Box component="form" onSubmit={(event) => void submit(event)}>
          <DialogTitle>Add {creation?.kind}</DialogTitle>
          <DialogContent>
            {error && message(error)}
            <TextField
              autoFocus
              required
              fullWidth
              margin="normal"
              name="title"
              label={creation?.kind === "list" ? "List name" : "Card title"}
              slotProps={{
                htmlInput: { maxLength: creation?.kind === "list" ? 160 : 500 },
              }}
            />
          </DialogContent>
          <DialogActions>
            <Button disabled={busy} onClick={() => setCreation(undefined)}>
              Cancel
            </Button>
            <Button disabled={busy} type="submit">
              {busy ? "Saving…" : "Create"}
            </Button>
          </DialogActions>
        </Box>
      </Dialog>
      <Dialog
        open={Boolean(cardId)}
        slotProps={{ transition: { onExited: () => {
          (cardLinks.current.get(closeFocusCard.current ?? '') ?? boardRefresh.current)?.focus();
          closeFocusCard.current = undefined;
        } } }}
        onClose={() => {
          if (!busy) closeCard();
        }}
        fullWidth
        maxWidth="sm"
      >
        <DialogTitle>Card details</DialogTitle>
        <DialogContent>
          {!card ? (
            <Alert severity="info">
              This card is unavailable in this board.
            </Alert>
          ) : (
            <><CardDetailEditor
              key={card.id}
              card={card}
              acknowledged={acknowledged}
              editable={Boolean(editable)}
              busy={busy}
              saved={saved}
              error={error ?? loadError}
              renderError={message}
              onSubmit={(event, version) => void submit(event, true, version)}
              onDiscard={discardAndLoad}
              onRefresh={() => setReload((value) => value + 1)}
            />
            {snapshot.access.canMove && snapshot.board.lifecycleState === "active"
              && snapshot.lists.some(column => column.list.lifecycleState === "active" && column.cards.some(item => item.id === card.id)) && <CardMoveControls
              key={`move-${card.id}`} card={card} snapshot={snapshot} disabled={busy || snapshotReading || !!loadError}
              onBusyChange={setBusy}
              onPreview={setMovePreview}
              onAcknowledged={() => { setSnapshotReading(true); setReload(value => value + 1); }}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}</>
          )}
        </DialogContent>
        <DialogActions>
          <Button disabled={busy} onClick={closeCard}>
            Close
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
