import { activityEvent } from '../features/kanban/activityTelemetry';

// Observe browser failures without reading/retaining Error, reason, stack or URLs.
// No retry, reload or state change: a command may already have completed.
export function installRuntimeExceptionObservers(production = import.meta.env.PROD) {
  if (!production) return () => {};
  const onError = (event: Event) => {
    if (!(event instanceof ErrorEvent)) return; // Resource load events aren't script exceptions.
    event.preventDefault(); // Suppress the browser's default private diagnostic in production.
    try { activityEvent('application_event_exception', 'exception'); } catch { /* Best effort only. */ }
  };
  const onRejection = (event: PromiseRejectionEvent) => {
    event.preventDefault();
    try { activityEvent('application_promise_exception', 'exception'); } catch { /* Best effort only. */ }
  };
  window.addEventListener('error', onError);
  window.addEventListener('unhandledrejection', onRejection);
  return () => {
    window.removeEventListener('error', onError);
    window.removeEventListener('unhandledrejection', onRejection);
  };
}
