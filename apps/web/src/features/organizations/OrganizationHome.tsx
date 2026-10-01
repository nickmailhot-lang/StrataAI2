import { useEffect, useRef, useState } from "react";
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
  WorkMutationIntent,
  WorkRequestError,
  WorkInputError,
} from "../../api/workManagement";

type OrganizationSummary = {
  organization: {
    id: string;
    name: string;
    description: string | null;
    status: number;
  };
  role: number;
};
type BoardSummary = { id: string; name: string; version: number };
type Discovery = {
  organizations: OrganizationSummary[];
  boards: BoardSummary[];
};

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
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);
  const mutation = useRef(new WorkMutationIntent());
  const [error, setError] = useState<Error>();
  const navigate = useNavigate();
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      const organizations = await workRequest<OrganizationSummary[]>(
        "/organizations",
        { signal: controller.signal },
      );
      if (
        organizationId &&
        !organizations.some((item) => item.organization.id === organizationId)
      )
        throw new WorkRequestError(404, null);
      const boards = organizationId
        ? await workRequest<BoardSummary[]>(
            `/organizations/${encodeURIComponent(organizationId)}/boards`,
            { signal: controller.signal },
          )
        : [];
      if (!controller.signal.aborted) {
        setData({ organizations, boards });
        setLoadError(undefined);
      }
    })().catch((reason: unknown) => {
      if (controller.signal.aborted) return;
      if (reason instanceof WorkRequestError && reason.status === 401)
        navigate("/login", { replace: true });
      else {
        setData(undefined);
        setLoadError(
          reason instanceof Error ? reason : new Error("Unable to load."),
        );
      }
    });
    return () => controller.abort();
  }, [organizationId, reload, navigate]);
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
            backgroundValue: "#0f4c81",
          },
        );
        navigate(`/app/${organizationId}/boards/${board.id}`);
      } else {
        const created = await workRequest<OrganizationSummary>(
          "/organizations",
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              name,
              description: String(form.get("description") ?? ""),
            }),
          },
        );
        navigate(`/app/${created.organization.id}`);
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
        {reason instanceof WorkRequestError || reason instanceof WorkInputError
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
      <Stack spacing={2}>
        <Stack direction="row" spacing={2}>
          <Button component={Link} to="/app">
            Organizations
          </Button>
          <Button component={Link} to="/app/profile" aria-label="Open profile">
            Profile
          </Button>
          <Button component={Link} to="/app/invitations">Invitations</Button>
        </Stack>
        <Typography variant="h4" component="h1">
          {organizationId
            ? (organization?.name ?? "Organization boards")
            : "Your organizations"}
        </Typography>
        {loadError ? (
          <>
            {failure(loadError)}
            <Button onClick={() => setReload((value) => value + 1)}>
              Retry
            </Button>
          </>
        ) : !data ? (
          <CircularProgress aria-label="Loading organizations" />
        ) : (
          <>
            {organization?.description && (
              <Typography>{organization.description}</Typography>
            )}
            <Stack direction="row" spacing={2}>
              {organizationId && organization?.status === 0 && ownRole !== undefined && ownRole <= 1 && (
                <Button component={Link} to={`/app/${organizationId}/settings`}>Organization settings</Button>
              )}
              {data.organizations.some(item => item.organization.id === organizationId && item.organization.status === 0 && item.role <= 1) && (
                <Button component={Link} to={`/app/${organizationId}/members`}>Organization members</Button>
              )}
              <Button
                onClick={() => {
                  setData(undefined);
                  setReload((value) => value + 1);
                }}
              >
                Refresh
              </Button>
              {(!organizationId || organization?.status === 0) && (
                <Button
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
                <Typography>No accessible boards yet.</Typography>
              ) : (
                data.boards.map((board) => (
                  <Button
                    key={board.id}
                    component={Link}
                    to={`/app/${organizationId}/boards/${board.id}`}
                    sx={{ justifyContent: "flex-start" }}
                  >
                    {board.name}
                  </Button>
                ))
              )
            ) : data.organizations.length === 0 ? (
              <Typography>
                You have no organizations yet. Create one to begin.
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
        <Dialog
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
        </Dialog>
      </Stack>
    </Container>
  );
}
