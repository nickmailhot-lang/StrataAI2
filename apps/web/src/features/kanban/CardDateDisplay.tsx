import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Chip, Stack, Typography } from '@mui/material';
import { CalendarToday, CheckCircle, Schedule, Warning } from '@mui/icons-material';
import { boundedWorkRead, workRequest, WorkRequestError, type WorkCard } from '../../api/workManagement';
import { watchIdentity } from '../auth/identityLive';
import { isNotificationProfile, type NotificationProfile } from '../notifications/notificationInbox';
import { cardDates, cardDueState, dateTimezone, formatCardDate, nextCardDateWake, type DueState } from './cardDates';

type Props = { card: WorkCard; organizationId: string; boardId: string; unavailable: boolean; boardTimezone?: string | null; onRefresh: () => void };
const labels: Record<DueState, string> = { UPCOMING: 'Upcoming', DUE_SOON: 'Due soon', DUE_TODAY: 'Due today', OVERDUE: 'Overdue', COMPLETE: 'Complete' };
export function CardDateDisplay(props: Props) {
  if (props.unavailable || !props.card.startAt && !props.card.dueAt) return null;
  return <CurrentDates key={`${props.organizationId}/${props.boardId}/${props.card.id}/${props.card.version}`} {...props} />;
}
function CurrentDates({ card, boardTimezone, onRefresh }: Props) {
  const [profile, setProfile] = useState<NotificationProfile>(); const [error, setError] = useState(false);
  const [now, setNow] = useState(Date.now); const [attempt, setAttempt] = useState(0);
  const subject = useRef<string | undefined>(undefined);
  const refresh = useRef(onRefresh); refresh.current = onRefresh;
  useEffect(() => {
    let active = true, reading = false, admissionRequired = false, queued = false;
    let stopIdentity: (() => void) | undefined;
    let controller: AbortController | undefined;
    const load = async () => {
      if (reading || !active || admissionRequired || document.visibilityState === 'hidden') return;
      reading = true; controller = new AbortController();
      try {
        const value = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
        if (!active) return;
        if (!isNotificationProfile(value)) throw new WorkRequestError(401, null);
        dateTimezone(value.timezone);
        if (subject.current && subject.current !== value.id) {
          admissionRequired = true; queued = false; stopIdentity?.(); stopIdentity = undefined;
          setProfile(undefined); setError(true); refresh.current(); return;
        }
        subject.current = value.id; setProfile(value); setError(false); setNow(Date.now());
        stopIdentity ??= watchIdentity({ subject: value.id, isProfile: isNotificationProfile, invalidate: check });
      } catch (reason) {
        if (active) { setProfile(undefined); setError(true); }
        if (active && reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) {
          admissionRequired = true; queued = false; stopIdentity?.(); stopIdentity = undefined;
        }
      } finally {
        reading = false;
        if (active && queued && !admissionRequired) { queued = false; void load(); }
      }
    };
    function check() {
      if (!active || admissionRequired || document.visibilityState === 'hidden') return;
      if (reading) queued = true; else void load();
    }
    void load();
    const timer = setInterval(check, 30_000); window.addEventListener('focus', check); window.addEventListener('online', check); document.addEventListener('visibilitychange', check);
    return () => { active = false; queued = false; controller?.abort(); stopIdentity?.(); clearInterval(timer); window.removeEventListener('focus', check); window.removeEventListener('online', check); document.removeEventListener('visibilitychange', check); };
  }, [attempt]);
  useEffect(() => {
    if (!profile) return;
    let zone: string;
    try { zone = dateTimezone(boardTimezone ?? profile.timezone); } catch { return; }
    const timer = setTimeout(() => setNow(Date.now()), nextCardDateWake([card], zone, now));
    return () => clearTimeout(timer);
  }, [profile, boardTimezone, card, now]);
  if (error) return <Alert severity="warning">Dates are unavailable. <Button onClick={() => { refresh.current(); setAttempt(value => value + 1); }}>Refresh dates</Button></Alert>;
  if (!profile) return <Typography role="status">Loading dates…</Typography>;
  try {
    const timezone = dateTimezone(boardTimezone ?? profile.timezone);
    const values = cardDates(card), state = cardDueState(values, timezone, now);
    const icon = state === 'COMPLETE' ? <CheckCircle /> : state === 'OVERDUE' ? <Warning /> : state === 'UPCOMING' ? <CalendarToday /> : <Schedule />;
    return <Stack component="section" aria-label="Card dates" spacing={1} sx={{ my: 2 }}>
      {values.startAt && <Typography>Starts {formatCardDate(values.startAt, true, profile.locale, timezone)}</Typography>}
      {values.dueAt && <Typography>Due {formatCardDate(values.dueAt, values.dueHasTime, profile.locale, timezone)}</Typography>}
      {state && <Chip icon={icon} label={labels[state]} color={state === 'OVERDUE' ? 'error' : state === 'COMPLETE' ? 'success' : 'default'} sx={{ alignSelf: 'flex-start' }} />}
      <Typography variant="caption">Viewing timezone: {timezone}. Date context: {values.dueTimezone}.{boardTimezone != null ? ' Board timezone policy.' : ''}</Typography>
    </Stack>;
  } catch { return <Alert severity="warning">Dates are unavailable. Refresh the Card to check its current dates.</Alert>; }
}
