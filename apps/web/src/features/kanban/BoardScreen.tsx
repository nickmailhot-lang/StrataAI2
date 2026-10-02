import { useCallback, useEffect, useEffectEvent, useRef, useState } from "react";
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
import { CardDateDisplay } from './CardDateDisplay';
import { CardDateEditor } from './CardDateEditor';
import { CardLabels } from './CardLabels';
import { CardAssignees } from './CardAssignees';
import { WatchControl } from '../notifications/WatchControl';
import { CardLabelPicker } from './CardLabelPicker';
import { CardMemberPicker } from './CardMemberPicker';
import { CardMemberIndicators } from './CardMemberIndicators';
import { CardLabelIndicators } from './CardLabelIndicators';
import { LabelCreateControl } from './LabelCreateControl';
import { LabelManageControl } from './LabelManageControl';
import { BoardFilterControl } from './BoardFilterControl';
import { filteredBoardCanvas, type BoardCanvasFilter } from './boardFilterCanvas';
import { CardMoveControls, type CardDropRequest } from "./CardMoveControls";
import { CardArchiveControl } from './CardArchiveControl';
import { CardDragItem, CardListEndTarget } from './CardDragItem';
import { ListPositionControls } from "./ListPositionControls";
import { ListRenameControl } from './ListRenameControl';
import { ListArchiveControl } from './ListArchiveControl';
import { ListCopyControl } from './ListCopyControl';
import { previewListMove, type ListMovePreview } from "./listMovePreview";
import { DndContext, PointerSensor, KeyboardSensor, closestCenter, pointerWithin, useSensor, useSensors } from '@dnd-kit/core';
import { ListDragColumn, ListEndTarget, type ListDropRequest } from './ListDragColumn';
import { listKeyboardCoordinates } from './listKeyboardCoordinates';
import { cardKeyboardCoordinates } from './cardKeyboardCoordinates';
import { listDragAnnouncements, listDragInstructions } from './listDragAccessibility';
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
  const [canvasFilter, setCanvasFilter] = useState<BoardCanvasFilter>();
  const refreshFilteredBoard = useCallback(() => { setSnapshotReading(true); setReload(value => value + 1); }, []);
  const [creation, setCreation] = useState<Creation>();
  const [operationBusy, setBusy] = useState(false);
  const [archiveRecovery, setArchiveRecovery] = useState(false);
  const [cardArchiveRecovery, setCardArchiveRecovery] = useState(false);
  const [copyRecovery, setCopyRecovery] = useState(false);
  const [labelRecovery, setLabelRecovery] = useState(false);
  const [labelManageRecovery, setLabelManageRecovery] = useState(false);
  const [assignmentRecovery, setAssignmentRecovery] = useState(false);
  const [memberRecovery, setMemberRecovery] = useState(false);
  const [dateRecovery, setDateRecovery] = useState(false);
  const otherBusy = operationBusy || archiveRecovery || cardArchiveRecovery || copyRecovery || labelRecovery || labelManageRecovery || assignmentRecovery || memberRecovery;
  const busy = otherBusy || dateRecovery;
  const [movePreview, setMovePreview] = useState<CardMovePreview>();
  const [listPreview, setListPreview] = useState<ListMovePreview>();
  const [listDrop, setListDrop] = useState<ListDropRequest>();
  const [cardDrop, setCardDrop] = useState<CardDropRequest>();
  const [cardRecovery, setCardRecovery] = useState(false);
  const [moveScope, setMoveScope] = useState(key);
  // Retire drop events when navigating away; returning must not resubmit one.
  if (moveScope !== key) {
    setMoveScope(key); setCardDrop(undefined); setListDrop(undefined);
    setCardRecovery(false); setMovePreview(undefined); setListPreview(undefined);
  }
  const updateCardRecovery = useCallback((_id: string, unresolved: boolean) => setCardRecovery(unresolved), []);
  const dragCard = useRef<{ cardId: string; version: number } | undefined>(undefined);
  const [listRecovery, setListRecovery] = useState(new Set<string>());
  const [renameRecovery, setRenameRecovery] = useState(new Set<string>());
  const updateRenameRecovery = useCallback((id: string, unresolved: boolean) => setRenameRecovery(previous => {
    if (previous.has(id) === unresolved) return previous;
    const next = new Set(previous); if (unresolved) next.add(id); else next.delete(id); return next;
  }), []);
  const updateListRecovery = useCallback((id: string, unresolved: boolean) => setListRecovery(previous => {
    if (previous.has(id) === unresolved) return previous;
    const next = new Set(previous); if (unresolved) next.add(id); else next.delete(id); return next;
  }), []);
  const dragList = useRef<{ listId: string; name: string; version: number } | undefined>(undefined);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 8 } }), useSensor(KeyboardSensor, { coordinateGetter: (event, args) => String(args.active).startsWith('card:') ? cardKeyboardCoordinates(event, args) : listKeyboardCoordinates(event, args) }));
  const mutation = useRef(new WorkMutationIntent());
  const activeRead = useRef<AbortController | undefined>(undefined);
  const reading = useRef(false);
  const [snapshotReading, setSnapshotReading] = useState(true);
  const cardLinks = useRef(new Map<string, HTMLAnchorElement>());
  const closeFocusCard = useRef<string | undefined>(undefined);
  const cardClose = useRef<HTMLButtonElement>(null);
  const canvasFocus = useRef<{ scope: string; cardId: string } | undefined>(undefined);
  useEffect(() => {
    const requested = canvasFocus.current;
    if (!requested) return;
    if (requested.scope !== key || cardId) { canvasFocus.current = undefined; return; }
    if (snapshotReading || busy) return;
    canvasFocus.current = undefined;
    if (requested.scope === key) (cardLinks.current.get(requested.cardId) ?? boardRefresh.current)?.focus();
  }, [snapshotReading, busy, cardId, key]);
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
          <WatchControl organizationId={snapshot.board.organizationId} boardId={snapshot.board.id} entityType="BOARD" entityId={snapshot.board.id}
            admitted={snapshot.access.canView && snapshot.board.lifecycleState === 'active'} disabled={busy || !!loadError} refreshing={snapshotReading}
            onReturnFocus={() => boardRefresh.current?.focus({ preventScroll: true })} />
          {snapshot.access.canAdminister && snapshot.board.lifecycleState === "active" && (
            <><Button component={Link} to={`/app/${organizationId}/boards/${boardId}/invite`}>Invite to Board</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/visibility`}>Board visibility</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/members`}>Board members</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/invitations`}>Board invitations</Button>
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/archived-lists`}>Archived lists</Button></>
          )}
          {snapshot.access.canEdit && snapshot.board.lifecycleState === 'active' &&
            <Button component={Link} to={`/app/${organizationId}/boards/${boardId}/archived-cards`}>Archived cards</Button>}
          <ListArchiveControl snapshot={snapshot}
            disabled={operationBusy || copyRecovery || labelRecovery || labelManageRecovery || snapshotReading || !!loadError || cardRecovery || !!cardId || !!creation}
            unavailableListIds={new Set([...listRecovery, ...renameRecovery])}
            onBusyChange={setBusy} onRecoveryChange={setArchiveRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }}
            onReturnFocus={() => boardRefresh.current?.focus({ preventScroll: true })} />
          <ListCopyControl snapshot={snapshot}
            disabled={operationBusy || archiveRecovery || cardArchiveRecovery || labelRecovery || labelManageRecovery || snapshotReading || !!loadError || cardRecovery || !!cardId || !!creation}
            unavailableListIds={new Set([...listRecovery, ...renameRecovery])}
            onBusyChange={setBusy} onRecoveryChange={setCopyRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }}
            onReturnFocus={() => boardRefresh.current?.focus({ preventScroll: true })} />
          <LabelCreateControl snapshot={snapshot} disabled={labelManageRecovery || operationBusy || archiveRecovery || cardArchiveRecovery || copyRecovery || snapshotReading || !!loadError || cardRecovery || !!cardId || !!creation}
            onBusyChange={setBusy} onRecoveryChange={setLabelRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }}
            onReturnFocus={() => boardRefresh.current?.focus({ preventScroll: true })} />
          <LabelManageControl snapshot={snapshot} disabled={operationBusy || archiveRecovery || cardArchiveRecovery || copyRecovery || labelRecovery || snapshotReading || !!loadError || cardRecovery || !!cardId || !!creation}
            onBusyChange={setBusy} onRecoveryChange={setLabelManageRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }}
            onReturnFocus={() => boardRefresh.current?.focus({ preventScroll: true })} />
          {editable && (
            <Button
              disabled={busy}
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
      <BoardFilterControl snapshot={snapshot} disabled={busy || snapshotReading || !!loadError || cardRecovery || listRecovery.size > 0 || renameRecovery.size > 0 || !!cardId || !!creation}
        onRefresh={refreshFilteredBoard} onCanvasChange={setCanvasFilter} />
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
      <DndContext sensors={sensors} autoScroll={{ canScroll: element => element.hasAttribute('data-kanban-scroll') }} collisionDetection={args => {
        const movingCard = String(args.active.id).startsWith('card:');
        const droppableContainers = args.droppableContainers.filter(value => {
          const cardTarget = String(value.id).startsWith('card:') || String(value.id).startsWith('card-end:');
          return movingCard === cardTarget;
        });
        return args.pointerCoordinates ? pointerWithin({ ...args, droppableContainers }) : closestCenter({ ...args, droppableContainers });
      }}
        accessibility={{ announcements: listDragAnnouncements(snapshot), screenReaderInstructions: listDragInstructions }} onDragStart={event => {
        dragCard.current = undefined; dragList.current = undefined;
        if (String(event.active.id).startsWith('card:')) {
          const item = snapshot.lists.flatMap(value => value.cards).find(value => `card:${value.id}` === event.active.id);
          if (item) dragCard.current = { cardId: item.id, version: item.version };
          return;
        }
        const column = snapshot.lists.find(value => value.list.id === event.active.id);
        if (column && Number.isSafeInteger(column.list.version)) dragList.current = { listId: column.list.id, name: column.list.name, version: column.list.version! };
      }} onDragCancel={() => { dragList.current = undefined; dragCard.current = undefined; }} onDragEnd={event => {
        const sourceCard = dragCard.current; dragCard.current = undefined;
        if (sourceCard) {
          if (!event.over || event.over.id === `card:${sourceCard.cardId}` || busy || snapshotReading || loadError || cardRecovery) return;
          const target = String(event.over.id);
          const column = snapshot.lists.find(value => value.list.lifecycleState === 'active' && (target === `card-end:${value.list.id}` || value.cards.some(item => target === `card:${item.id}`)));
          if (!column || !snapshot.access.canMove || snapshot.board.lifecycleState !== 'active') return;
          setCardDrop({ ...sourceCard, destination: column.list.id, before: target.startsWith('card:') ? target.slice(5) : '', nonce: crypto.randomUUID() });
          return;
        }
        const source = dragList.current; dragList.current = undefined;
        if (!source || !event.over || event.over.id === source.listId || busy || snapshotReading || loadError) return;
        setListDrop({ ...source, before: event.over.id === 'list-end' ? '' : String(event.over.id), nonce: crypto.randomUUID() });
      }}>
      <Box
        aria-label="Kanban board"
        data-kanban-scroll
        sx={{
          display: "grid",
          gridAutoFlow: "column",
          gridAutoColumns: { xs: "82vw", sm: 320 },
          gap: 2,
          overflowX: "auto",
          pb: 2,
        }}
      >
        {(canvasFilter ? filteredBoardCanvas(snapshot, canvasFilter) : previewListMove(previewCardMove(snapshot, movePreview), listPreview)).lists.map((column) => (
          <ListDragColumn
            key={column.list.id}
            id={column.list.id} name={column.list.name}
            disabled={busy || snapshotReading || !!loadError || listRecovery.has(column.list.id) || renameRecovery.has(column.list.id)}
            available={!canvasFilter && snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && column.list.lifecycleState === 'active' && Number.isSafeInteger(column.list.version) && Number(column.list.version) > 0}
          >
            <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
            <Typography id={`list-name-${column.list.id}`} variant="h6" component="h3" sx={{ minWidth: 0, overflowWrap: 'anywhere' }}>
              {column.list.name}
            </Typography>
            <ListRenameControl list={column.list} snapshot={snapshot}
              disabled={busy || snapshotReading || !!loadError || listRecovery.has(column.list.id)}
              onBusyChange={setBusy} onRecoveryChange={updateRenameRecovery}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />
            </Stack>
            {!canvasFilter && <WatchControl organizationId={snapshot.board.organizationId} boardId={snapshot.board.id} entityType="LIST" entityId={column.list.id}
              admitted={snapshot.access.canView && snapshot.board.lifecycleState === 'active' && column.list.lifecycleState === 'active'}
              disabled={busy || !!loadError} refreshing={snapshotReading} onReturnFocus={() => boardRefresh.current?.focus({ preventScroll: true })} />}
            {snapshot.access.canMove && snapshot.board.lifecycleState === "active" && column.list.lifecycleState === "active" && <ListPositionControls
              list={column.list} snapshot={snapshot} disabled={!!canvasFilter || busy || snapshotReading || !!loadError || renameRecovery.has(column.list.id)} onBusyChange={setBusy}
              onPreview={setListPreview}
              onRecoveryChange={updateListRecovery}
              dropRequest={listDrop?.listId === column.list.id ? listDrop : undefined}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}
            <Stack spacing={1} sx={{ mt: 2 }}>
              {column.cards.map((item) => (
                <CardDragItem key={item.id} id={item.id} title={item.title}
                  disabled={busy || snapshotReading || !!loadError || cardRecovery || !!cardId}
                  available={!canvasFilter && snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && column.list.lifecycleState === 'active' && Number.isSafeInteger(item.version) && item.version > 0}>
                <Card
                  key={item.id}
                  component={Link}
                  aria-label={item.title}
                  ref={(node: HTMLAnchorElement | null) => {
                    if (node) cardLinks.current.set(item.id, node);
                    else cardLinks.current.delete(item.id);
                  }}
                  to={`${boardPath}/cards/${item.id}`}
                  state={{ cardOverlay: true }}
                  sx={{
                    display: 'block',
                    color: "inherit",
                    textDecoration: "none",
                    "&:focus-visible": {
                      outline: "3px solid",
                      outlineColor: "primary.main",
                    },
                  }}
                >
                  <CardContent>{item.title}<CardLabelIndicators preview={snapshot.cardLabels?.[item.id]} /><CardMemberIndicators version={item.version} preview={snapshotReading || loadError ? undefined : snapshot.cardMembers?.[item.id]} /></CardContent>
                </Card>
                </CardDragItem>
              ))}
            </Stack>
            {snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && column.list.lifecycleState === 'active' && <CardListEndTarget id={column.list.id} name={column.list.name} disabled={!!canvasFilter || busy || snapshotReading || !!loadError || cardRecovery || !!cardId} />}
            {column.cards.length === 0 && (
              <Typography sx={{ my: 2 }}>{canvasFilter ? 'No matching Cards on this page.' : 'No cards yet.'}</Typography>
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
          </ListDragColumn>
        ))}
        {snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && <ListEndTarget disabled={!!canvasFilter || busy || snapshotReading || !!loadError} />}
      </Box>
      </DndContext>
      {cardDrop && snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && (() => {
        const moved = snapshot.lists.filter(column => column.list.lifecycleState === 'active').flatMap(column => column.cards).find(value => value.id === cardDrop.cardId);
        return moved && <CardMoveControls key={`canvas-move-${moved.id}`} card={moved} snapshot={snapshot}
          dropRequest={cardDrop} disabled={busy || snapshotReading || !!loadError} onRecoveryChange={updateCardRecovery}
          onBusyChange={setBusy} onPreview={setMovePreview}
          onAcknowledged={() => { canvasFocus.current = { scope: key, cardId: moved.id }; setSnapshotReading(true); setReload(value => value + 1); }}
          onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />;
      })()}
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
        disableRestoreFocus
        slotProps={{ transition: { onExited: () => {
          (cardLinks.current.get(closeFocusCard.current ?? '') ?? boardRefresh.current)?.focus({ preventScroll: true });
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
            <CardDateDisplay card={card} organizationId={snapshot.board.organizationId} boardId={snapshot.board.id}
              unavailable={snapshotReading || !!loadError} onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />
            <CardDateEditor card={card} organizationId={snapshot.board.organizationId} boardId={snapshot.board.id}
              listId={snapshot.lists.find(column => column.cards.some(item => item.id === card.id))!.list.id}
              editable={Boolean(editable)} disabled={otherBusy || cardRecovery} unavailable={snapshotReading || !!loadError}
              onBusyChange={setBusy} onRecoveryChange={setDateRecovery}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />
            {cardId && <CardLabels key={`labels-${card.id}`} organizationId={snapshot.board.organizationId}
              boardId={snapshot.board.id} cardId={card.id} version={card.version} unavailable={snapshotReading || !!loadError}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}
            {cardId && <CardAssignees key={`assignees-${card.id}`} organizationId={snapshot.board.organizationId}
              boardId={snapshot.board.id} cardId={card.id} version={card.version} unavailable={snapshotReading || !!loadError}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}
            {snapshot.access.canMove && snapshot.board.lifecycleState === "active"
              && snapshot.lists.some(column => column.list.lifecycleState === "active" && column.cards.some(item => item.id === card.id)) && <CardMoveControls
              key={`move-${card.id}`} card={card} snapshot={snapshot} disabled={busy || snapshotReading || !!loadError || cardRecovery}
              onBusyChange={setBusy}
              onPreview={setMovePreview}
              onAcknowledged={() => { setSnapshotReading(true); setReload(value => value + 1); }}
              onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}</>
          )}
          {cardId && <WatchControl organizationId={snapshot.board.organizationId} boardId={snapshot.board.id} entityType="CARD" entityId={cardId}
            admitted={snapshot.access.canView && snapshot.board.lifecycleState === 'active' && !!card &&
              snapshot.lists.some(column => column.list.lifecycleState === 'active' && column.cards.some(item => item.id === cardId))}
            disabled={busy || !!loadError} refreshing={snapshotReading} onReturnFocus={() => cardClose.current?.focus({ preventScroll: true })} />}
          {cardId && <CardArchiveControl key={`archive-${cardId}`} cardId={cardId} card={card} snapshot={snapshot}
            disabled={operationBusy || archiveRecovery || copyRecovery || assignmentRecovery || memberRecovery || cardRecovery || snapshotReading || !!loadError}
            onBusyChange={setBusy} onRecoveryChange={setCardArchiveRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }}
            onAcknowledged={() => { canvasFocus.current = { scope: key, cardId };
              setSnapshotReading(true); setReload(value => value + 1); closeCard(); }} />}
          {cardId && <CardLabelPicker key={`label-picker-${cardId}`} cardId={cardId} card={card} snapshot={snapshot}
            disabled={operationBusy || archiveRecovery || cardArchiveRecovery || copyRecovery || labelRecovery || labelManageRecovery || memberRecovery || cardRecovery || snapshotReading || !!loadError}
            onBusyChange={setBusy} onRecoveryChange={setAssignmentRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}
          {cardId && <CardMemberPicker key={`member-picker-${cardId}`} cardId={cardId} card={card} snapshot={snapshot}
            disabled={operationBusy || archiveRecovery || cardArchiveRecovery || copyRecovery || labelRecovery || labelManageRecovery || assignmentRecovery || cardRecovery || snapshotReading || !!loadError}
            onBusyChange={setBusy} onRecoveryChange={setMemberRecovery}
            onRefresh={() => { setSnapshotReading(true); setReload(value => value + 1); }} />}
        </DialogContent>
        <DialogActions>
          <Button ref={cardClose} disabled={busy} onClick={closeCard}>
            Close
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
