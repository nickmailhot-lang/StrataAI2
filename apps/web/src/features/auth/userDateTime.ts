export function formatUserDateTime(value: string, preferences: { locale: string; timezone: string }): string | undefined {
  // Instants require an explicit offset; offset-free input would use the browser's local zone.
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) return undefined;
  // Validate the supplied calendar independently of its offset. Date parsing
  // otherwise rolls February 30 or hour 24 into a different displayed date.
  const calendar = new Date(`${match[1]}Z`);
  if (!Number.isFinite(calendar.getTime()) || calendar.toISOString().slice(0, 19) !== match[1]) return undefined;
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
