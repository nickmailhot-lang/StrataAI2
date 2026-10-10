import { useEffect, useLayoutEffect, useRef, useState } from "react";
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Container,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  workRequest,
  boundedWorkRead,
  WorkMutationIntent,
  WorkRequestError,
  WorkInputError,
} from "../../api/workManagement";
import { isNotificationProfile } from "../notifications/notificationInbox";
import { watchOrganizationBoards } from "../kanban/organizationBoardLive";
import { watchOrganizationMetadata } from './organizationMetadataLive';
import { watchOrganizationLifecycle, type OrganizationLifecycleState } from './organizationLifecycleLive';
import { OrganizationCreationDialog } from './OrganizationCreationDialog';
import { organizationTypeLabel } from './organizationTypes';
import { NavigationConfirmation } from '../../app/NavigationConfirmation';

type OrganizationSummary = {
  organization: {
    id: string;
    name: string;
    description: string | null;
    status: number;
    type?: string;
  };
  role: number;
};
type BoardSummary = { id: string; name: string; version: number };
type Discovery = {
  organizations: OrganizationSummary[];
  boards: BoardSummary[];
  nextCursor: string | null;
};
function summary(value: unknown): value is OrganizationSummary {
  if (!value || typeof value !== "object") return false;
  const item = value as Partial<OrganizationSummary>; const org = item.organization;
  return !!org && typeof org.id === "string" && org.id.length > 0 && typeof org.name === "string"
    && org.name.length > 0 && org.name.length <= 160 && (org.description === null || typeof org.description === "string")
    && org.status === 0 && Number.isInteger(item.role) && item.role! >= 0 && item.role! <= 2;
}
function directory(value: unknown, after?: string): { items: OrganizationSummary[]; nextCursor: string | null } {
  if (!value || typeof value !== "object") throw new WorkRequestError(503, null);
  const page = value as { items?: unknown; nextCursor?: unknown };
  if (!Array.isArray(page.items) || page.items.length > 50 || !page.items.every(summary)
    || new Set(page.items.map(item => item.organization.id)).size !== page.items.length) throw new WorkRequestError(503, null);
  const cursor = page.nextCursor;
  if (cursor !== null && (typeof cursor !== "string" || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(cursor)
    || cursor === "00000000-0000-0000-0000-000000000000" || after && cursor <= after)) throw new WorkRequestError(503, null);
  return { items: page.items, nextCursor: cursor as string | null };
}


function boardDirectory(value: unknown, organizationId: string, after?: string): { items: BoardSummary[]; nextCursor: string | null } {
  const uuid = (value: unknown): value is string => typeof value === "string"
    && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value)
    && value !== "00000000-0000-0000-0000-000000000000";
  if (!value || typeof value !== "object") throw new WorkRequestError(503, null);
  const page = value as { organizationId?: unknown; items?: unknown; nextCursor?: unknown };
  if (page.organizationId !== organizationId || !Array.isArray(page.items) || page.items.length > 50)
    throw new WorkRequestError(503, null);
  const items: BoardSummary[] = [];
  for (const value of page.items) {
    if (!value || typeof value !== "object") throw new WorkRequestError(503, null);
    const row = value as Partial<BoardSummary>;
    if (!uuid(row.id) || typeof row.name !== "string" || !row.name.trim() || row.name.length > 160
      || !Number.isSafeInteger(row.version) || row.version! < 1
      || after && row.id <= after || items.length && row.id <= items[items.length - 1].id)
      throw new WorkRequestError(503, null);
    items.push(row as BoardSummary);
  }
  const cursor = page.nextCursor;
  if (cursor !== null && (!uuid(cursor) || after && cursor <= after
    || !items.length || cursor !== items[items.length - 1].id)) throw new WorkRequestError(503, null);
  return { items, nextCursor: cursor as string | null };
}

// PRD-01/03/04: server-authorized discovery and persisted organization/board creation.
export function OrganizationHome() {
  const { organizationId } = useParams();
  return (
    <DiscoveryScreen
      key={organizationId ?? "home"}
      organizationId={organizationId}
    />
  );
}
function DiscoveryScreen({ organizationId }: { organizationId?: string }) {
  const [data, setData] = useState<Discovery>();
  const [loadError, setLoadError] = useState<Error>();
  const [reload, setReload] = useState(0);
  const [cursor, setCursor] = useState<string>();
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);
  const mutation = useRef(new WorkMutationIntent());
  const pageFocus = useRef(false);
  const creationFocus = useRef(false);
  const createButton = useRef<HTMLButtonElement>(null);
  const firstPage = useRef<HTMLButtonElement>(null);
  const nextPage = useRef<HTMLButtonElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const [error, setError] = useState<Error>();
  const actor = useRef<string | undefined>(undefined);
  const read = useRef<AbortController | undefined>(undefined);
  const [liveActor, setLiveActor] = useState<string>();
  const [lifecycleActor, setLifecycleActor] = useState<string>();
  const [lifecycleState, setLifecycleState] = useState<OrganizationLifecycleState>();
  const lifecycle = useRef<OrganizationLifecycleState | undefined>(undefined);
  const [liveNotice, setLiveNotice] = useState<string>();
  const navigate = useNavigate();
  useEffect(() => {
    if (lifecycleState === 'PENDING' || lifecycleState === 'COMPLETED') return;
    const controller = new AbortController();
    read.current = controller;
    void boundedWorkRead(async (signal) => {
      const before = await workRequest<unknown>("/me", { signal });
      if (!isNotificationProfile(before) || actor.current && actor.current !== before.id)
        throw new WorkRequestError(401, null);
      if (!signal.aborted && organizationId) {
        // Lifecycle admission is independent of ordinary graph admission. A
        // fresh deep link must still recover a ready terminal source after 404.
        actor.current = before.id; setLifecycleActor(before.id);
      }
      let organizations: OrganizationSummary[]; let nextCursor: string | null = null;
      if (organizationId) {
        const current = await workRequest<unknown>(`/organizations/${encodeURIComponent(organizationId)}`, { signal });
        if (!summary(current) || current.organization.id !== organizationId) throw new WorkRequestError(404, null);
        organizations = [current];
      } else {
        const page = directory(await workRequest<unknown>(`/organizations/directory${cursor ? `?after=${encodeURIComponent(cursor)}` : ""}`,
          { signal }), cursor);
        organizations = page.items; nextCursor = page.nextCursor;
      }
      let boards: BoardSummary[] = [];
      if (organizationId) {
        const page = boardDirectory(await workRequest<unknown>(
          `/organizations/${encodeURIComponent(organizationId)}/boards/directory${cursor ? `?after=${encodeURIComponent(cursor)}` : ""}`,
          { signal }), organizationId, cursor);
        boards = page.items; nextCursor = page.nextCursor;
      }
      const after = await workRequest<unknown>("/me", { signal });
      if (!isNotificationProfile(after) || after.id !== before.id) throw new WorkRequestError(401, null);
      if (!signal.aborted) {
        actor.current = after.id; setLiveActor(after.id);
        if (organizationId) setLifecycleActor(after.id);
        setData({ organizations, boards, nextCursor });
        setLoadError(undefined);
        setLiveNotice(value => value ? "Current Board access checked." : undefined);
      }
    }, controller.signal).catch((reason: unknown) => {
      if (controller.signal.aborted) return;
      setLiveActor(undefined); setData(undefined);
      if (reason instanceof WorkRequestError && reason.status === 401) {
        actor.current = undefined; setLifecycleActor(undefined); lifecycle.current = undefined; setLifecycleState(undefined);
        navigate("/login", { replace: true });
      } else {
        setData(undefined);
        setLoadError(
          reason instanceof Error ? reason : new Error("Unable to load."),
        );
      }
    });
    return () => { controller.abort(); if (read.current === controller) read.current = undefined; };
  }, [organizationId, cursor, reload, navigate, lifecycleState]);
  useEffect(() => {
    if (!organizationId || !lifecycleActor) return;
    const withdraw = () => {
      read.current?.abort(); setData(undefined); setLoadError(undefined); setError(undefined); setCreating(false); setLiveActor(undefined);
    };
    return watchOrganizationLifecycle({ organizationId, userId: lifecycleActor,
      update: state => {
        if (state === 'ACTIVE') return;
        lifecycle.current = state; setLifecycleState(state); withdraw();
        setLiveNotice(state === 'COMPLETED' ? 'Organization deletion confirmed complete.' : 'Organization deletion is being confirmed.');
      },
      unavailable: () => {
        lifecycle.current = undefined; setLifecycleState(undefined); withdraw();
        setLiveNotice('Checking current Organization lifecycle and access.'); setReload(value => value + 1);
      },
      accountUnavailable: () => {
        actor.current = undefined; setLifecycleActor(undefined); lifecycle.current = undefined; setLifecycleState(undefined);
        withdraw(); setLiveNotice(undefined); navigate('/login', { replace: true });
      },
    });
  }, [organizationId, lifecycleActor, navigate]);
  useEffect(() => {
    if (!organizationId || !liveActor) return;
    const recover = (message: string) => {
      read.current?.abort(); setData(undefined); setLoadError(undefined);
      setCreating(false); setLiveNotice(message); setReload(value => value + 1);
    };
    const stopBoards = watchOrganizationBoards({ organizationId, userId: liveActor, audience: 'discovery',
      invalidate: () => recover("Boards changed. Checking current access."),
      reset: () => recover("Checking current Board access."),
      unavailable: () => recover("Live updates interrupted. Checking current access.") });
    const stopMetadata = watchOrganizationMetadata({ organizationId, userId: liveActor,
      invalidate: () => recover('Organization changed. Checking current access.'),
      reset: () => recover('Checking current Organization metadata.'),
      unavailable: () => recover('Live Organization updates interrupted. Checking current access.') });
    return () => { stopBoards(); stopMetadata(); };
  }, [organizationId, liveActor]);
  useLayoutEffect(() => {
    if (!data || !pageFocus.current) return;
    pageFocus.current = false;
    (cursor ? firstPage.current : nextPage.current ?? heading.current)?.focus();
  }, [data, cursor]);
  useEffect(() => {
    if (data && creationFocus.current) { creationFocus.current = false; createButton.current?.focus(); }
  }, [data]);
  const organization = data?.organizations.find(
    (item) => item.organization.id === organizationId,
  )?.organization;
  const ownRole = data?.organizations.find(item => item.organization.id === organizationId)?.role;
  async function create(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const form = new FormData(event.currentTarget);
    const name = String(form.get("name") ?? "").trim();
    if (!name) {
      setError(new WorkInputError("Enter a name."));
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      if (organizationId) {
        const board = await mutation.current.send<{ id: string }>(
          "/boards",
          "POST",
          {
            organizationId,
            name,
            description: String(form.get("description") ?? ""),
            visibility: String(form.get("visibility") ?? "PRIVATE"),
            backgroundType: "COLOR",
            backgroundValue: "blue",
          },
        );
        if (lifecycle.current === 'PENDING' || lifecycle.current === 'COMPLETED') return;
        navigate(`/app/${organizationId}/boards/${board.id}`);
      } else {
        return;
      }
      setCreating(false);
    } catch (reason) {
      if (reason instanceof WorkRequestError && reason.status === 401)
        navigate("/login", { replace: true });
      else
        setError(
          reason instanceof Error ? reason : new Error("Unable to save."),
        );
    } finally {
      setBusy(false);
    }
  }
  function failure(reason: Error) {
    return (
      <Alert severity="error">
        {organizationId && reason instanceof WorkRequestError && [403, 404].includes(reason.status)
          ? "Access to this Organization surface is unavailable."
          : reason instanceof WorkRequestError || reason instanceof WorkInputError
          ? reason.message
          : "Unable to contact StrataAI2. Please try again."}
        {reason instanceof WorkRequestError &&
          reason.correlationId &&
          ` Reference: ${reason.correlationId}`}
      </Alert>
    );
  }
  return (
    <Container maxWidth="lg" sx={{ py: 3 }}>
      <Stack spacing={2} sx={{ overflowWrap: "anywhere" }}>
        {!organizationId && data && liveActor && !loadError &&
          <NavigationConfirmation key={`navigation-global-${liveActor}`} target={{ kind: 'context', organization: null }} />}
        {liveNotice && <Typography role="status" aria-live="polite" aria-atomic="true">{liveNotice}</Typography>}
        <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: "wrap" }}>
          <Button component={Link} to="/app">
            Organizations
          </Button>
          <Button component={Link} to="/app/profile" aria-label="Open profile">
            Profile
          </Button>
          <Button component={Link} to="/app/invitations">Invitations</Button>
        </Stack>
        <Typography variant="h4" component="h1" ref={heading} tabIndex={-1}>
          {organizationId
            ? (organization?.name ?? "Organization boards")
            : "Your organizations"}
        </Typography>
        {lifecycleState === 'PENDING' || lifecycleState === 'COMPLETED' ? (
          <Typography>Organization content is unavailable.</Typography>
        ) : loadError ? (
          <>
            {failure(loadError)}
            <Button onClick={() => setReload((value) => value + 1)}>
              Retry
            </Button>
          </>
        ) : !data ? (
          <CircularProgress aria-label={organizationId ? "Loading boards" : "Loading organizations"} />
        ) : (
          <>
            {organization && organizationTypeLabel(organization.type) && (
              <Typography>Organization type: {organizationTypeLabel(organization.type)}</Typography>
            )}
            {organization?.description && (
              <Typography>{organization.description}</Typography>
            )}
            <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: "wrap" }}>
              {organizationId && organization?.status === 0 && <Button component={Link} to={`/app/${organizationId}/archived-boards`}>Archived boards</Button>}
              {organizationId && organization?.status === 0 && ownRole !== undefined && <Button component={Link} to={`/app/${organizationId}/leave`}>Leave Organization</Button>}
              {organizationId && organization?.status === 0 && ownRole === 0 && <Button component={Link} to={`/app/${organizationId}/delete`}>Request Organization deletion</Button>}
              {organizationId && organization?.status === 0 && ownRole !== undefined && ownRole <= 1 && (
                <Button component={Link} to={`/app/${organizationId}/settings`}>Organization settings</Button>
              )}
              {data.organizations.some(item => item.organization.id === organizationId && item.organization.status === 0 && item.role <= 1) && (
                <Button component={Link} to={`/app/${organizationId}/members`}>Organization members</Button>
              )}
              <Button disabled={creating && !organizationId}
                onClick={() => {
                  setData(undefined);
                  setReload((value) => value + 1);
                }}
              >
                Refresh
              </Button>
              {(!organizationId || organization?.status === 0) && (
                <Button
                  ref={createButton} disabled={creating}
                  onClick={() => {
                    setError(undefined);
                    setCreating(true);
                  }}
                >
                  Create {organizationId ? "board" : "organization"}
                </Button>
              )}
            </Stack>
            {organizationId ? (
              data.boards.length === 0 ? (
                <Typography>{cursor ? "No accessible boards on this page. Return to the first page to check earlier boards." : "No accessible boards yet."}</Typography>
              ) : (
                data.boards.map((board) => (
                  <Button
                    key={board.id}
                    component={Link}
                    to={`/app/${organizationId}/boards/${board.id}`}
                    aria-label={board.name.replace(/\s+/g, " ").trim()}
                    sx={{ justifyContent: "flex-start" }}
                  >
                    {board.name}
                  </Button>
                ))
              )
            ) : data.organizations.length === 0 ? (
              <Typography>
                {cursor || data.nextCursor ? "No available organizations on this page. Continue to check later organizations." : "You have no organizations yet. Create one to begin."}
              </Typography>
            ) : (
              data.organizations.map((item) => (
                <Button
                  key={item.organization.id}
                  component={Link}
                  to={`/app/${item.organization.id}`}
                  sx={{ justifyContent: "flex-start" }}
                >
                  {item.organization.name}
                </Button>
              ))
            )}
          </>
        )}
        {(data || cursor) && <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: "wrap" }}>
          {cursor && <Button ref={firstPage} disabled={creating && !organizationId} onClick={() => { pageFocus.current = true; read.current?.abort(); setData(undefined); setLoadError(undefined); setCreating(false); setCursor(undefined); }}>First {organizationId ? "Board" : "Organization"} page</Button>}
          {data?.nextCursor && <Button ref={nextPage} disabled={creating && !organizationId} onClick={() => { pageFocus.current = true; read.current?.abort(); setData(undefined); setCreating(false); setCursor(data.nextCursor!); }}>Next {organizationId ? "Board" : "Organization"} page</Button>}
        </Stack>}
        {creating && !organizationId && liveActor && <OrganizationCreationDialog actorId={liveActor}
          onCancel={() => { creationFocus.current = true; setCreating(false); setData(undefined); setReload(value => value + 1); }}
          onCreated={id => { setCreating(false); navigate(`/app/${id}`); }} />}
        {creating && organizationId && <Dialog
          open={creating}
          onClose={() => {
            if (!busy) setCreating(false);
          }}
          fullWidth
          maxWidth="sm"
        >
          <Box component="form" onSubmit={(event) => void create(event)}>
            <DialogTitle>
              Create {organizationId ? "board" : "organization"}
            </DialogTitle>
            <DialogContent>
              {error && failure(error)}
              <TextField
                name="name"
                label="Name"
                autoFocus
                required
                fullWidth
                margin="normal"
                slotProps={{ htmlInput: { maxLength: 160 } }}
                disabled={busy}
              />
              <TextField
                name="description"
                label="Description"
                fullWidth
                multiline
                minRows={2}
                margin="normal"
                disabled={busy}
              />
              {organizationId && (
                <TextField
                  name="visibility"
                  label="Visibility"
                  select
                  fullWidth
                  margin="normal"
                  defaultValue="PRIVATE"
                  disabled={busy}
                >
                  <MenuItem value="PRIVATE">Private</MenuItem>
                  <MenuItem value="ORGANIZATION">Organization members</MenuItem>
                  <MenuItem value="PUBLIC">Public read-only</MenuItem>
                </TextField>
              )}
            </DialogContent>
            <DialogActions>
              <Button disabled={busy} onClick={() => setCreating(false)}>
                Cancel
              </Button>
              <Button disabled={busy} type="submit">
                {busy ? "Saving…" : "Create"}
              </Button>
            </DialogActions>
          </Box>
        </Dialog>}
      </Stack>
    </Container>
  );
}
