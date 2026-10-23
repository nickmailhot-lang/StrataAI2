import { useId, useLayoutEffect, useRef, useState } from "react";
import { Alert, Box, Button, InputBase, TextField, Typography } from "@mui/material";
import { WorkRequestError, type WorkCard } from "../../api/workManagement";
import { CardDescriptionPreview } from './CardDescriptionPreview';
import { workExperienceEnabled } from '../../app/workExperience';

type Props = {
  externalSave?: boolean;
  onDraftStateChange?: (state: { dirty: boolean; canSave: boolean }) => void;
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

// PRD-09 / PRD-22: incoming versions never silently replace a dirty draft.
// The parent keys this editor by card identity, not by its mutable revision.
export function CardDetailEditor({
  externalSave,
  onDraftStateChange,
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
  const titleInput = useRef<HTMLInputElement>(null);
  const titleId = useId();
  // Focus after the complete dialog DOM mutation, rather than forcing layout
  // while React is still inserting its remaining controls. The editor is keyed
  // by Card identity; revisions and dirty-draft updates never steal focus.
  useLayoutEffect(() => {
    if (titleInput.current && !titleInput.current.disabled) titleInput.current.focus();
  }, []);
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
  const canSave = editable && !busy && !conflict;
  useLayoutEffect(() => { onDraftStateChange?.({ dirty, canSave }); }, [dirty, canSave, onDraftStateChange]);
  useLayoutEffect(() => () => onDraftStateChange?.({ dirty: false, canSave: false }), [onDraftStateChange]);
  return (
    <Box
      component="form"
      id={externalSave ? 'card-core-form' : undefined}
      sx={workExperienceEnabled ? { p: 2, bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: 2 } : undefined}
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
      {!editable && dirty && <Alert severity="warning">
        Editing is unavailable. Your unsaved changes are shown here and have not been saved.
      </Alert>}
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
      <Box sx={{ mt: 2, mb: 1 }}>
        <Typography component="label" htmlFor={titleId} variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
          Card title <span aria-hidden="true">*</span>
        </Typography>
        <InputBase
          id={titleId}
          inputRef={titleInput}
          name="title"
          inputProps={{ maxLength: 500 }}
          required
          fullWidth
          value={draft.title}
          onChange={(event) => setDraft({ ...draft, title: event.target.value })}
          disabled={!editable || busy}
          sx={{ minHeight: 56, px: 2, border: 1, borderColor: 'divider', borderRadius: 1,
            ...(workExperienceEnabled && { fontSize: '1.25rem', fontWeight: 600 }),
            '&.Mui-focused': { outline: '3px solid', outlineColor: 'primary.main', outlineOffset: 1 },
            '& input.Mui-disabled': { color: 'text.secondary', WebkitTextFillColor: 'currentColor' } }}
        />
      </Box>
      <TextField
        name="description"
        label="Description"
        helperText="Markdown: headings, bold, italic, links, lists, code and blockquotes."
        multiline
        // Keep the raw source scrollable at a stable height. Autosizing a long
        // description forces synchronous shadow-textarea layout during mount;
        // the complete formatted preview remains immediately available below.
        slotProps={{ input: { inputComponent: 'textarea' }, htmlInput: { rows: 6 },
          // Instructions remain readable even when the associated editor is
          // disabled for a read-only visitor or during a pending operation.
          formHelperText: !editable || busy
            ? { sx: { '&.Mui-disabled': { color: 'text.secondary' } } } : undefined }}
        fullWidth
        margin="normal"
        value={draft.description}
        onChange={(event) =>
          setDraft({ ...draft, description: event.target.value })
        }
        disabled={!editable || busy}
      />
      <CardDescriptionPreview description={draft.description} />
      {editable && !externalSave && (
        <Button type="submit" disabled={busy || conflict}>
          Save card
        </Button>
      )}
    </Box>
  );
}
