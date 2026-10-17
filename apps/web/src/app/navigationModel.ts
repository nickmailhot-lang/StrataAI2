export type NavigationLink = { id: string; label: string; to: string; current?: boolean };
export type ShellNavigation = NavigationLink | { id: string; label: string; unavailable: true };

// This models discovery only. Each page and API still admits its own operations.
export function internalNavigation(surface: 'INTERNAL' | 'PORTAL', organizationId: string, pathname: string): ShellNavigation[] {
  if (surface !== 'INTERNAL' || !organizationId) return [];
  const scope = `/app/${encodeURIComponent(organizationId)}`;
  return [
    { id: 'organizations', label: 'Organizations', to: '/app', current: pathname === '/app' },
    { id: 'boards', label: 'Work', to: scope, current: pathname === scope || pathname === `${scope}/` || pathname.startsWith(`${scope}/boards/`) || pathname === `${scope}/search` },
    { id: 'notifications', label: 'Notifications', to: `${scope}/notifications`, current: pathname === `${scope}/notifications` },
  ];
}

export type OrganizationNavigationSource = { organization: { id: string; status: number }; role: number };

// Call only with the currently admitted Organization summary. Unknown, withdrawn
// or foreign-scope summaries do not retain links from an earlier membership.
export function organizationNavigation(organizationId: string | undefined, source: OrganizationNavigationSource | undefined): NavigationLink[] {
  if (!organizationId || source?.organization.id !== organizationId || source.organization.status !== 0
    || ![0, 1, 2].includes(source.role)) return [];
  const scope = `/app/${encodeURIComponent(organizationId)}`;
  const links = [
    { id: 'archived-boards', label: 'Archived boards', to: `${scope}/archived-boards` },
    { id: 'leave', label: 'Leave Organization', to: `${scope}/leave` },
  ];
  if (source.role === 0) links.push({ id: 'delete', label: 'Request Organization deletion', to: `${scope}/delete` });
  if (source.role <= 1) links.push(
    { id: 'operational-lifecycle', label: 'Organization lifecycle', to: `${scope}/operational-lifecycle` },
    { id: 'settings', label: 'Organization settings', to: `${scope}/settings` },
    { id: 'configuration', label: 'Organization configuration', to: `${scope}/configuration` },
    { id: 'retention-policies', label: 'Retention policies', to: `${scope}/retention-policies` },
    { id: 'retention-holds', label: 'Retention holds', to: `${scope}/retention-holds` },
    { id: 'members', label: 'Organization members', to: `${scope}/members` },
  );
  return links;
}
