import { useState } from "react";
import { Alert, Box, Button, TextField, Typography } from "@mui/material";
import { WorkRequestError, type WorkCard } from "../../api/workManagement";

type Props = {
  card: WorkCard;
  acknowledged?: WorkCard;
  editable: boolean;
  busy: boolean;
  saved: boolean;
  error?: Error;
  renderError: (error: Error) => React.ReactNode;
  onSubmit: (
    event: React.FormEvent<HTMLFormElement>,
    expectedVersion: number,
  ) => void;
  onDiscard: () => Promise<WorkCard | undefined>;
  onRefresh: () => void;
};
const initial = (card: WorkCard) => ({
  base: card,
  title: card.title,
  description: card.description ?? "",
});

// PRD-09 / PRD-17: incoming versions never silently replace a dirty draft.
// The parent keys this editor by card identity, not by its mutable revision.
export function CardDetailEditor({
  card,
  acknowledged,
  editable,
  busy,
  saved,
  error,
  renderError,
  onSubmit,
  onDiscard,
  onRefresh,
}: Props) {
  const [draft, setDraft] = useState(() => initial(card));
  const dirty =
    draft.title !== draft.base.title ||
    draft.description !== (draft.base.description ?? "");
  const accepted =
    acknowledged?.id === card.id && acknowledged.version > draft.base.version
      ? acknowledged
      : undefined;
  const refreshed =
    !dirty && card.version > draft.base.version ? card : undefined;
  // Conditional adjustment during render preserves both focus and selection.
  // An acknowledged local save may precede the refreshed snapshot; older
  // snapshots cannot subsequently roll that acknowledgment back.
  if (accepted || refreshed) setDraft(initial(accepted ?? refreshed!));
  const incomingConflict =
    dirty && card.version > draft.base.version && !accepted;
  const conflict =
    incomingConflict ||
    (error instanceof WorkRequestError && error.status === 409);
  return (
    <Box
      component="form"
      onSubmit={(event) => {
        if (conflict || busy || !editable) {
          event.preventDefault();
          return;
        }
        onSubmit(event, draft.base.version);
      }}
    >
      {saved && !dirty && <Typography role="status">Changes saved.</Typography>}
      {error && renderError(error)}
      <Button disabled={busy} onClick={onRefresh}>
        Refresh card
      </Button>
      {incomingConflict &&
        !(error instanceof WorkRequestError && error.status === 409) && (
          <Alert severity="warning">
            This card changed elsewhere. Your draft is preserved. Load the
            latest card before saving.
          </Alert>
        )}
      {conflict && (
        <Button
          disabled={busy}
          onClick={() => {
            void onDiscard()
              .then((latest) => {
                if (latest) setDraft(initial(latest));
              })
              .catch(() => {
                /* Parent displays a fixed error; preserve this draft. */
              });
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
        value={draft.title}
        onChange={(event) => setDraft({ ...draft, title: event.target.value })}
        disabled={!editable || busy}
      />
      <TextField
        name="description"
        label="Description"
        multiline
        minRows={3}
        fullWidth
        margin="normal"
        value={draft.description}
        onChange={(event) =>
          setDraft({ ...draft, description: event.target.value })
        }
        disabled={!editable || busy}
      />
      {editable && (
        <Button type="submit" disabled={busy || conflict}>
          Save card
        </Button>
      )}
    </Box>
  );
}
