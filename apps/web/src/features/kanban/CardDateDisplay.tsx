import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Chip, Stack, Typography } from '@mui/material';
import { CalendarToday, CheckCircle, Schedule, Warning } from '@mui/icons-material';
import { boundedWorkRead, workRequest, type WorkCard } from '../../api/workManagement';
import { isNotificationProfile, type NotificationProfile } from '../notifications/notificationInbox';
import { cardDates, cardDueState, dateTimezone, formatCardDate, type DueState } from './cardDates';

type Props = { card: WorkCard; organizationId: string; boardId: string; unavailable: boolean; boardTimezone?: string | null; onRefresh: () => void };
const labels: Record<DueState, string> = { UPCOMING: 'Upcoming', DUE_SOON: 'Due soon', DUE_TODAY: 'Due today', OVERDUE: 'Overdue', COMPLETE: 'Complete' };
export function CardDateDisplay(props: Props) {
  if (props.unavailable || !props.card.startAt && !props.card.dueAt) return null;
  return <CurrentDates key={`${props.organizationId}/${props.boardId}/${props.card.id}/${props.card.version}`} {...props} />;
}
function CurrentDates({ card, boardTimezone, onRefresh }: Props) {
  const [profile, setProfile] = useState<NotificationProfile>(); const [error, setError] = useState(false);
  const [now, setNow] = useState(Date.now); const [attempt, setAttempt] = useState(0);
  const refresh = useRef(onRefresh); refresh.current = onRefresh;
  useEffect(() => {
    let active = true, reading = false, subject: string | undefined;
    let controller: AbortController | undefined;
    const load = async () => {
      if (reading || !active || document.visibilityState === 'hidden') return;
      reading = true; controller = new AbortController();
      try {
        const value = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
        if (!active) return;
        if (!isNotificationProfile(value)) throw new Error('Invalid date viewer');
        dateTimezone(value.timezone);
        if (subject && subject !== value.id) { setProfile(undefined); setError(true); refresh.current(); return; }
        subject = value.id; setProfile(value); setError(false); setNow(Date.now());
      } catch {
        if (active) { setProfile(undefined); setError(true); }
      } finally { reading = false; }
    };
    void load();
    const check = () => { void load(); };
    const timer = setInterval(check, 30_000); window.addEventListener('focus', check); document.addEventListener('visibilitychange', check);
    return () => { active = false; controller?.abort(); clearInterval(timer); window.removeEventListener('focus', check); document.removeEventListener('visibilitychange', check); };
  }, [attempt]);
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
