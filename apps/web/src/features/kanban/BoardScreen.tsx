import { useEffect, useRef, useState } from "react";
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
} from "../../api/workManagement";
type Loaded = { key: string; snapshot?: BoardSnapshot; error?: Error };
type Creation = { kind: "list" | "card"; listId?: string };
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
  const mutation = useRef(new WorkMutationIntent());
  const [failure, setFailure] = useState<{ cardId?: string; error: Error }>();
  const [savedFor, setSavedFor] = useState<{ cardId?: string }>();
  const error = failure?.cardId === cardId ? failure?.error : undefined;
  const saved = Boolean(savedFor) && savedFor?.cardId === cardId;
  function setError(next?: Error) {
    setFailure(next ? { cardId, error: next } : undefined);
  }
  function setSaved(next: boolean) {
    setSavedFor(next ? { cardId } : undefined);
  }
  const navigate = useNavigate();
  const location = useLocation();
  useEffect(() => {
    const controller = new AbortController();
    void loadBoard(organizationId, boardId, controller.signal)
      .then((snapshot) => {
        if (!controller.signal.aborted) setLoaded({ key, snapshot });
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted)
          setLoaded({
            key,
            error:
              reason instanceof Error
                ? reason
                : new Error("Unable to load this board."),
          });
      });
    return () => controller.abort();
  }, [organizationId, boardId, key, reload]);
  const snapshot = loaded?.key === key ? loaded.snapshot : undefined;
  const loadError = loaded?.key === key ? loaded.error : undefined;
  const card = snapshot?.lists
    .flatMap((column) => column.cards)
    .find((item) => item.id === cardId);
  const editable =
    snapshot?.access.canEdit && snapshot.board.lifecycleState === "active";
  const boardPath = `/app/${organizationId}/boards/${boardId}`;
  function closeCard() {
    if (location.state?.cardOverlay) navigate(-1);
    else navigate(boardPath, { replace: true });
  }
  async function submit(event: React.FormEvent<HTMLFormElement>, edit = false) {
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
    setError(undefined);
    setSaved(false);
    try {
      if (edit && card)
        await mutation.current.send(`/cards/${card.id}`, "PATCH", {
          title,
          description: String(form.get("description") ?? ""),
          version: card.version,
        });
      else if (creation?.kind === "list")
        await mutation.current.send(`/boards/${boardId}/lists`, "POST", {
          name: title,
        });
      else if (creation?.listId)
        await mutation.current.send(`/lists/${creation.listId}/cards`, "POST", {
          title,
        });
      setCreation(undefined);
      setSaved(true);
      setReload((value) => value + 1);
    } catch (reason) {
      setError(
        reason instanceof WorkRequestError
          ? reason
          : new Error("Unable to save."),
      );
    } finally {
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
  if (loadError)
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
        <Stack direction="row">
          <Button
            disabled={busy}
            onClick={() => setReload((value) => value + 1)}
          >
            Refresh board
          </Button>
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
        {snapshot.lists.map((column) => (
          <Box
            key={column.list.id}
            sx={{ bgcolor: "grey.100", borderRadius: 2, p: 2, minHeight: 240 }}
          >
            <Typography variant="h6" component="h3">
              {column.list.name}
            </Typography>
            <Stack spacing={1} sx={{ mt: 2 }}>
              {column.cards.map((item) => (
                <Card
                  key={item.id}
                  component={Link}
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
            <Box
              component="form"
              key={`${card.id}/${card.version}`}
              onSubmit={(event) => void submit(event, true)}
            >
              {saved && <Typography role="status">Changes saved.</Typography>}
              {error && message(error)}
              {error instanceof WorkRequestError && error.status === 409 && (
                <Button
                  disabled={busy}
                  onClick={() => {
                    setError(undefined);
                    setLoaded(undefined);
                    setReload((value) => value + 1);
                  }}
                >
                  Discard edits and load latest card
                </Button>
              )}
              <TextField
                autoFocus
                name="title"
                label="Card title"
                slotProps={{ htmlInput: { maxLength: 500 } }}
                required
                fullWidth
                margin="normal"
                defaultValue={card.title}
                disabled={!editable || busy}
              />
              <TextField
                name="description"
                label="Description"
                multiline
                minRows={3}
                fullWidth
                margin="normal"
                defaultValue={card.description ?? ""}
                disabled={!editable || busy}
              />
              {editable && (
                <Button
                  type="submit"
                  disabled={
                    busy ||
                    (error instanceof WorkRequestError && error.status === 409)
                  }
                >
                  Save card
                </Button>
              )}
            </Box>
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
