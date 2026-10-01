export function formatUserDateTime(value: string, preferences: { locale: string; timezone: string }): string | undefined {
  // Instants require an explicit offset; offset-free input would use the browser's local zone.
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value)) return undefined;
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return undefined;
  try {
    return new Intl.DateTimeFormat(preferences.locale, {
      timeZone: preferences.timezone,
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
    }).format(date);
  } catch { return undefined; }
}
