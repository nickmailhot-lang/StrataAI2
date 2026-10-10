import { Box, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { configurationNumberFields, configurationTextFields, type ConfigurationDraft } from './organizationConfigurationDraft';
import type { OrganizationConfiguration } from './organizationConfiguration';

export type IntakeOption = { id: string; name: string };
export function OrganizationConfigurationForm({ draft, onChange, disabled, boards, lists, onBoardChange, onMoreBoards, onMoreLists, intakeBusy = false }:
  { draft: ConfigurationDraft; onChange: (value: ConfigurationDraft) => void; disabled: boolean; boards: IntakeOption[]; lists: IntakeOption[];
    onBoardChange: (id: string) => void; onMoreBoards?: () => void; onMoreLists?: () => void; intakeBusy?: boolean }) {
  function field(key: keyof ConfigurationDraft, value: string) { onChange({ ...draft, [key]: value }); }
  return <Stack spacing={3}>
    <Typography>Required legal details are entered explicitly. Configuration does not establish legal compliance.</Typography>
    <Stack spacing={2}>
      {configurationTextFields.map(([key, label, maximum]) => <TextField key={key} label={label} value={draft[key]} disabled={disabled}
        required={['legalName', 'jurisdiction', 'timezone'].includes(key)} multiline={key === 'civicAddress'}
        type={key === 'insuranceRenewalDate' ? 'date' : 'text'}
        slotProps={{ htmlInput: { maxLength: maximum }, inputLabel: key === 'insuranceRenewalDate' ? { shrink: true } : undefined }}
        onChange={event => field(key, event.target.value)} />)}
      {configurationNumberFields.map(([key, label, maximum]) => <TextField key={key} label={label} value={draft[key]} disabled={disabled}
        slotProps={{ htmlInput: { inputMode: 'numeric', pattern: '[0-9]*', maxLength: 7 } }}
        helperText={`Optional; whole number from 1 to ${maximum}.`} onChange={event => field(key, event.target.value)} />)}
    </Stack>
    <Box component="fieldset" sx={{ minWidth: 0, p: 2 }} disabled={disabled || intakeBusy}>
      <Typography component="legend">Intake destination</Typography>
      <Stack spacing={2}>
        <TextField select label="Intake Board" value={draft.intakeBoardId} disabled={disabled || intakeBusy} onChange={event => onBoardChange(event.target.value)}>
          <MenuItem value="">No intake Board</MenuItem>
          {draft.intakeBoardId && !boards.some(row => row.id === draft.intakeBoardId) && <MenuItem value={draft.intakeBoardId} disabled>Previously configured Board; verify availability</MenuItem>}
          {boards.map(row => <MenuItem key={row.id} value={row.id}>{row.name}</MenuItem>)}
        </TextField>
        <TextField select label="Intake List" value={draft.intakeListId} disabled={disabled || intakeBusy || !draft.intakeBoardId}
          onChange={event => field('intakeListId', event.target.value)}>
          <MenuItem value="">No intake List</MenuItem>
          {draft.intakeListId && !lists.some(row => row.id === draft.intakeListId) && <MenuItem value={draft.intakeListId} disabled>Previously configured List; verify availability</MenuItem>}
          {lists.map(row => <MenuItem key={row.id} value={row.id}>{row.name}</MenuItem>)}
        </TextField>
        {onMoreBoards && <Button disabled={disabled || intakeBusy} onClick={onMoreBoards}>More intake Boards</Button>}
        {onMoreLists && <Button disabled={disabled || intakeBusy} onClick={onMoreLists}>More intake Lists</Button>}
        <Typography variant="body2">Choose active records in this Organization. Changing the Board clears the selected List.</Typography>
      </Stack>
    </Box>
    <Box component="fieldset" sx={{ minWidth: 0, p: 2 }} disabled={disabled}>
      <Typography component="legend">Emergency contacts</Typography>
      <Stack spacing={2}>
        {(draft.emergencyContacts ?? []).map((row, index) => <Stack spacing={1} key={index}>
          {(['name', 'email', 'phone'] as const).map(key => <TextField key={key} label={`Emergency contact ${index + 1} ${key}`} value={row[key]} disabled={disabled}
            slotProps={{ htmlInput: { maxLength: key === 'name' ? 160 : key === 'email' ? 320 : 80 } }}
            onChange={event => onChange({ ...draft, emergencyContacts: draft.emergencyContacts!.map((item, at) => at === index ? { ...item, [key]: event.target.value } : item) })} />)}
          <Button disabled={disabled} onClick={() => onChange({ ...draft, emergencyContacts: draft.emergencyContacts!.filter((_, at) => at !== index) })}>Remove emergency contact {index + 1}</Button>
        </Stack>)}
        <Button disabled={disabled || (draft.emergencyContacts?.length ?? 0) >= 32}
          onClick={() => onChange({ ...draft, emergencyContacts: [...draft.emergencyContacts ?? [], { name: '', email: '', phone: '' }] })}>Add emergency contact</Button>
      </Stack>
    </Box>
    {(['defaultCategories', 'defaultPriorities'] as const).map(key => <Box component="fieldset" key={key} sx={{ minWidth: 0, p: 2 }} disabled={disabled}>
      <Typography component="legend">{key === 'defaultCategories' ? 'Default categories' : 'Default priorities'}</Typography>
      <Stack spacing={1}>
        {(draft[key] ?? []).map((value, index) => <Stack key={index} spacing={1}>
          <TextField label={`${key === 'defaultCategories' ? 'Category' : 'Priority'} ${index + 1}`} value={value} disabled={disabled}
            slotProps={{ htmlInput: { maxLength: 160 } }} onChange={event => onChange({ ...draft, [key]: draft[key]!.map((item, at) => at === index ? event.target.value : item) })} />
          <Button disabled={disabled} onClick={() => onChange({ ...draft, [key]: draft[key]!.filter((_, at) => at !== index) })}>Remove {key === 'defaultCategories' ? 'category' : 'priority'} {index + 1}</Button>
        </Stack>)}
        <Button disabled={disabled || (draft[key]?.length ?? 0) >= 32}
          onClick={() => onChange({ ...draft, [key]: [...draft[key] ?? [], ''] })}>Add {key === 'defaultCategories' ? 'category' : 'priority'}</Button>
      </Stack>
    </Box>)}
    <Box component="fieldset" sx={{ minWidth: 0, p: 2 }} disabled={disabled}>
      <Typography component="legend">Jurisdiction policies</Typography>
      <Stack spacing={2}>
        <Typography variant="body2">Record the reviewed value, source and notes. No local law is inferred.</Typography>
        {(draft.jurisdictionPolicies ?? []).map((row, index) => <Stack spacing={1} key={index}>
          {(['key', 'value', 'source', 'notes'] as const).map(key => <TextField key={key} label={`Policy ${index + 1} ${key}`} value={row[key]} disabled={disabled}
            multiline={key !== 'key'} slotProps={{ htmlInput: { maxLength: key === 'key' ? 64 : key === 'source' ? 1000 : 2000 } }}
            onChange={event => onChange({ ...draft, jurisdictionPolicies: draft.jurisdictionPolicies!.map((item, at) => at === index ? { ...item, [key]: event.target.value } : item) })} />)}
          <Button disabled={disabled} onClick={() => onChange({ ...draft, jurisdictionPolicies: draft.jurisdictionPolicies!.filter((_, at) => at !== index) })}>Remove policy {index + 1}</Button>
        </Stack>)}
        <Button disabled={disabled || (draft.jurisdictionPolicies?.length ?? 0) >= 32}
          onClick={() => onChange({ ...draft, jurisdictionPolicies: [...draft.jurisdictionPolicies ?? [], { key: '', value: '', source: '', notes: '' }] })}>Add jurisdiction policy</Button>
      </Stack>
    </Box>
  </Stack>;
}

export function ConfigurationValues({ configuration, boards, lists }: { configuration: OrganizationConfiguration; boards: IntakeOption[]; lists: IntakeOption[] }) {
  return <Stack spacing={1} sx={{ overflowWrap: 'anywhere' }}>
    <Box component="dl" sx={{ m: 0 }}>
      {[...configurationTextFields, ...configurationNumberFields].map(([key, label]) => <Box key={key} sx={{ mb: 1 }}>
        <Typography component="dt" sx={{ fontWeight: 600 }}>{label}</Typography>
        <Typography component="dd" sx={{ m: 0, whiteSpace: 'pre-wrap' }}>{configuration[key] ?? 'Not configured'}</Typography>
      </Box>)}
      <Typography component="dt" sx={{ fontWeight: 600 }}>Intake Board</Typography>
      <Typography component="dd" sx={{ m: 0 }}>{configuration.intakeBoardId ? boards.find(row => row.id === configuration.intakeBoardId)?.name ?? 'Recorded Board; availability not confirmed' : 'Not configured'}</Typography>
      <Typography component="dt" sx={{ fontWeight: 600 }}>Intake List</Typography>
      <Typography component="dd" sx={{ m: 0 }}>{configuration.intakeListId ? lists.find(row => row.id === configuration.intakeListId)?.name ?? 'Recorded List; availability not confirmed' : 'Not configured'}</Typography>
    </Box>
    <Typography sx={{ fontWeight: 600 }}>Emergency contacts</Typography>
    {!configuration.emergencyContacts?.length && <Typography>None configured</Typography>}
    {configuration.emergencyContacts?.map((row, index) => <Typography key={index}>{row.name}; email: {row.email ?? 'Not configured'}; phone: {row.phone ?? 'Not configured'}</Typography>)}
    <Typography>Default categories: {configuration.defaultCategories?.join(', ') || 'None configured'}</Typography>
    <Typography>Default priorities: {configuration.defaultPriorities?.join(', ') || 'None configured'}</Typography>
    <Typography sx={{ fontWeight: 600 }}>Jurisdiction policies</Typography>
    {!configuration.jurisdictionPolicies?.length && <Typography>None configured</Typography>}
    {configuration.jurisdictionPolicies?.map(row => <Box key={row.key} sx={{ whiteSpace: 'pre-wrap' }}>
      <Typography sx={{ fontWeight: 600 }}>{row.key}</Typography><Typography>Value: {row.value}</Typography>
      <Typography>Source: {row.source}</Typography><Typography>Notes: {row.notes ?? 'Not configured'}</Typography>
    </Box>)}
  </Stack>;
}
