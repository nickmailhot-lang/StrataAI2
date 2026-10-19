# Shared navigation contract

UX-002 incremental implementation. `app/navigationModel.ts` supplies destination definitions to the existing shell and Organization home. No new route, endpoint, permission or role is introduced.

## Scope and authority

Internal shell links require existing INTERNAL SurfaceAdmission. They expose only the existing Organizations, Work and Notifications destinations. Work retains the existing Organization Board-directory route; current directory, Board/Card/admin descendants and Card search select it. PORTAL input cannot construct an internal menu. The separately admitted public Board and terminal recovery fallbacks remain outside internal navigation.

The live 2026-10-17 Work-navigation amendment omits unavailable Meetings, Correspondence, Documents and Owners & Units entries, replacing the earlier disabled “Not available yet” hints. Future features must add a working, capability-reviewed route; no placeholder is repointed to Organization home. This focused adoption reuses the existing UX draft's navigation model while its broader action disclosures and Card presentation remain separate work.

Historical PR #100 navigation wording, retained during integration:

Internal shell links require existing INTERNAL SurfaceAdmission. They expose only the existing Organizations, Work (the existing Board directory) and Notifications destinations. Current Board/Card/admin subroutes select the Work section. PORTAL input cannot construct an internal menu. The separately admitted public Board and terminal recovery fallbacks remain outside internal navigation.

Meetings, Correspondence, Documents and Owners & Units remain specified in the backlog but are omitted from primary navigation until implemented. They render no dead link or disabled primary entry. The Work label changes no route or canonical Board/Card entity name. Future features must add a working, capability-reviewed route rather than repoint a placeholder to Organization home.

## Organization-specific discovery

OrganizationHome passes only its currently admitted summary to `organizationNavigation`. The existing before/after account check, summary validation, scope match, directory admission, live reset/withdrawal and lifecycle rules remain the authority. The menu model keeps no cached role and performs no HTTP request or subscription.

| Authoritative Organization role | Existing destinations retained |
| --- | --- |
| 0 (Owner) | Archived Boards, leave, deletion review, operational lifecycle, settings, configuration, retention policies/holds, members |
| 1 (Admin) | Same administrative links except owner-only deletion review |
| 2 (Member) | Archived Boards and departure review |
| Unknown, foreign scope, missing summary, inactive lifecycle | No Organization-specific links |

These are the current Organization membership values, not a governance hierarchy. Board administration still derives from the Board snapshot's `canAdminister`, editing from `canEdit`, and reading from `canView`. President/Manager/acting capacity and future multi-role dashboards must use their canonical feature contracts; an Organization role must not imply them. UI discovery is never authorization for a direct URL or API action.

## Withdrawal and continuity

When OrganizationHome withdraws canonical data, privileged links disappear in the same render as the protected name/content. An aborted/late older read cannot restore those links through a separate navigation cache. A confirmed lower-role read yields only that role's existing links. No unresolved deletion/departure receipt is moved under ordinary shell admission.

The Organization actions disclosure retains existing labels, order and return paths. Shared shell menu, page-level Organization actions and Board-owned controls have distinct scopes; do not copy Board or Organization capabilities into a global role menu. The route inventory and the 80-feature backlog map define where future integrations belong.

## Evidence and remaining scope

Table-driven tests cover Owner/Admin/Member, missing/foreign/inactive/invalid source, Portal exclusion, current-section selection and unavailable destinations. The OrganizationHome integration test exercises synchronous live withdrawal and re-admission after a downgrade. Existing page/router/surface tests remain required.

This is an incremental UX-002 slice. Global account-page consolidation, governed roles/acting capacity and complete multi-role menu acceptance remain pending their existing feature contracts and explicit review. The change adds no independent permission polling or realtime connection and does not interrupt independent backend/feature delivery.
