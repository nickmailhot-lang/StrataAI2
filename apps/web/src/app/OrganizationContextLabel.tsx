import { Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../api/workManagement';
import { isNotificationProfile, notificationUuid } from '../features/notifications/notificationInbox';
import { watchOrganizationMetadata } from '../features/organizations/organizationMetadataLive';

type ContextName = { organizationId: string; name: string };

function currentName(value: unknown, organizationId: string): string {
  const summary = value as { organization?: { id?: unknown; name?: unknown; status?: unknown }; role?: unknown } | null;
  const organization = summary?.organization;
  if (!organization || !notificationUuid(organization.id) || organization.id.toLowerCase() !== organizationId
    || typeof organization.name !== 'string' || !organization.name.trim() || organization.name.length > 160
    || organization.status !== 0 || !Number.isInteger(summary?.role) || (summary!.role as number) < 0 || (summary!.role as number) > 2) {
    throw new WorkRequestError(503, null);
  }
  return organization.name;
}

// Context presentation never admits the protected route or grants an action.
// Each name belongs to a fresh, actor-bound canonical read of this scope.
export function OrganizationContextLabel() {
  const { organizationId } = useParams();
  const scope = notificationUuid(organizationId) ? organizationId.toLowerCase() : undefined;
  return <AuthorizedName key={scope ?? 'unscoped'} scope={scope} />;
}

function AuthorizedName({ scope }: { scope?: string }) {
  const [context, setContext] = useState<ContextName>();
  const name = context && context.organizationId === scope ? context.name : undefined;

  useEffect(() => {
    if (!scope) return;
    let disposed = false, generation = 0, networkAvailable = navigator.onLine !== false;
    let request: AbortController | undefined;
    let subscribedActor: string | undefined;
    let stopMetadata: (() => void) | undefined;
    const stop = () => {
      stopMetadata?.(); stopMetadata = undefined; subscribedActor = undefined;
    };
    async function refresh() {
      if (disposed) return;
      const active = ++generation;
      request?.abort();
      setContext(undefined);
      if (!networkAvailable || navigator.onLine === false || document.visibilityState === 'hidden') return;
      const controller = new AbortController(); request = controller;
      try {
        const admitted = await boundedWorkRead(async signal => {
          const before = await workRequest<unknown>('/me', { signal });
          if (!isNotificationProfile(before)) throw new WorkRequestError(401, null);
          const actor = before.id.toLowerCase();
          const summary = await workRequest<unknown>(`/organizations/${scope}`, {
            signal, headers: { 'X-StrataAI-Expected-Actor': actor },
          });
          const label = currentName(summary, scope!);
          const after = await workRequest<unknown>('/me', { signal });
          if (!isNotificationProfile(after) || after.id.toLowerCase() !== actor) throw new WorkRequestError(401, null);
          return { actor, name: label };
        }, controller.signal);
        if (disposed || active !== generation || controller.signal.aborted) return;
        if (subscribedActor !== admitted.actor) {
          stop(); subscribedActor = admitted.actor;
          const invalidate = () => { void refresh(); };
          stopMetadata = watchOrganizationMetadata({ organizationId: scope!, userId: admitted.actor,
            invalidate, reset: invalidate, unavailable: invalidate });
        }
        if (!disposed && active === generation && !controller.signal.aborted) {
          setContext({ organizationId: scope!, name: admitted.name });
        }
      } catch {
        if (!disposed && active === generation) setContext(undefined);
      }
    }
    const focus = () => { stop(); void refresh(); };
    const withdraw = () => {
      ++generation; request?.abort(); stop(); setContext(undefined);
    };
    const offline = () => { networkAvailable = false; withdraw(); };
    const online = () => { networkAvailable = true; stop(); void refresh(); };
    const visibility = () => {
      if (document.visibilityState === 'hidden') {
        withdraw();
      } else { void refresh(); }
    };
    window.addEventListener('focus', focus);
    window.addEventListener('offline', offline);
    window.addEventListener('online', online);
    document.addEventListener('visibilitychange', visibility);
    void refresh();
    return () => {
      disposed = true; ++generation; request?.abort(); stop();
      window.removeEventListener('focus', focus);
      window.removeEventListener('offline', offline);
      window.removeEventListener('online', online);
      document.removeEventListener('visibilitychange', visibility);
    };
  }, [scope]);

  return <Typography component="span" variant="body2" aria-live="polite" title={name}
    sx={{ order: { xs: 1, sm: 0 }, flexBasis: { xs: '100%', sm: 'auto' }, flexShrink: 1,
      minWidth: 0, maxWidth: { xs: '100%', sm: '20rem' }, mr: { xs: 0, sm: 1 }, display: 'block' }}>
    {name ? <>Organization: <span>{name}</span></> : 'Organization'}
  </Typography>;
}
