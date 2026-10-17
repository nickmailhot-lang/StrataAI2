# Existing route and discovery inventory

Source: `apps/web/src/app/App.tsx`, `InternalAppShell.tsx`, `OrganizationHome.tsx`, `BoardScreen.tsx` and `PortalShell.tsx` at the UX-001 baseline and additive shell slice. This inventories existing routes; proposed domain menu slots are separately marked “Planned” in [the backlog map](backlog-map.md). No planned slot becomes a route in this change.

`:org`, `:board` and `:card` below abbreviate the router's `:organizationId`, `:boardId` and `:cardId`. Role/capability notes describe current presentation; each route's HTTP operations remain independently authorized. This inventory cannot grant permissions.

| Route / family | Current shell / boundary | Existing discovery / workflow | UX-002 integration rule |
| --- | --- | --- | --- |
| `/`, `/login` | Root redirect / AuthPage | Sign-in; account registration | Preserve current identity and safe return behavior |
| `/forgot-password`, `/reset-password`, `/verify-email` | Independent identity pages | Recovery / verification requests and link completion | Preserve original-command/token recovery and expiry |
| `/invitation` | Independent invitation-link review | Explicit invitation proof review / acceptance | No auto-accept; preserve account/surface confirmation |
| `/app` | OrganizationHome outside scoped shell | Organization list, select/create, global profile/invitations/lifecycle links | Global navigation composition needs a separate safe migration |
| `/app/profile` | Global ProfilePage | OrganizationHome “Open profile” | Same account workflow as scoped profile, no duplicate feature |
| `/app/invitations` | Global InvitationsPage | OrganizationHome “Invitations” | Keep recipient acceptance distinct from issued-history administration |
| `/app/lifecycle` | Global lifecycle directory | Unscoped OrganizationHome “Organization lifecycle management” | Independent lifecycle discovery can outlive ordinary graph access |
| `/app/:org/delete` | Independently admitted OrganizationDeletePage | Active Organization owner deletion-review link | Must retain original deletion receipt when ordinary surface access ends |
| `/app/:org/leave` | Independently admitted OrganizationLeavePage | Current member departure-review link | Must retain original departure recovery after membership ends |
| `/app/:org/operational-lifecycle` | Independent lifecycle page | Active Organization Owner/Admin lifecycle link | Do not move unresolved terminal recovery under ordinary admission |
| `/app/:org` | InternalAppShell + INTERNAL SurfaceAdmission; separately admitted denied fallback | Select Organization; drawer “Boards”; active Board directory | Preserve terminal home/lifecycle fallback and canonical scope |
| `/app/:org/settings`, `configuration` | InternalAppShell | Active Organization home links for returned ownRole 0/1 | Preserve Owner/Admin presentation and server refusal |
| `/app/:org/retention-policies`, `retention-holds` | InternalAppShell | Active Organization home links for returned ownRole 0/1 | Existing policy/hold review is partial PRD-70, not full retention enforcement |
| `/app/:org/members` | InternalAppShell | Active Organization home link when returned directory role <= 1 | Reuse current administration capability; no new local role model |
| `/app/:org/invite`, `invitations` | InternalAppShell | Organization membership/invitation administration | Keep issuance/history separate from recipient acceptance |
| `/app/:org/archived-boards` | InternalAppShell | Active Organization home “Archived boards” | Preserve lifecycle-aware read, restore and consent rules |
| `/app/:org/search` | InternalAppShell | Header “Search Cards” | Existing Card search is not full PRD-56 domain search |
| `/app/:org/notifications` | InternalAppShell | Header shortcut and shared drawer “Notifications” | Notification → authorized source → Back; PRD-58 broader domain extension remains separate |
| `/app/:org/profile` | InternalAppShell | Header profile shortcut | Reuse account profile; scope changes do not imply another identity |
| `/app/:org/boards/:board` | InternalAppShell; independently authorized denied/public fallback | Board directory, stable source links | Preserve public read fallback without internal navigation |
| `/app/:org/boards/:board/cards/:card` | Same BoardScreen / Card detail | Stable Card link opens detail within Board context | Preserve Back, scroll/focus and independent Card authorization |
| Board suffix `invite`, `visibility`, `date-policy`, `members`, `invitations`, `archived-lists` | InternalAppShell | BoardScreen controls currently use returned Board capabilities | Do not expose admin controls merely because INTERNAL surface is admitted |
| Board suffix `archived-cards` | InternalAppShell | BoardScreen capability-gated archived Card directory | Retain per-action edit/restore permissions |
| `/portal/:org` | Separate PortalShell + PORTAL SurfaceAdmission | Explicit Portal entry; Owner documents placeholder; Browse disabled | No internal menu; only explicitly published permitted portal content |

## Current menu contract

The shared internal menu activates only Organizations (`/app`), Work (`/app/:org`) and Notifications. Work retains the Board-directory route and identifies current Board/Card descendants and Card search. Header shortcuts keep Card search and scoped profile separate. The live 2026-10-17 Work amendment omits unavailable Meetings, Correspondence, Documents and Owners & Units entries on all devices; this replaces the baseline's disabled “Not available yet” hints. No route, permission or role changes. Broader Organization-action and Card-presentation adoption remains separate work.

Organization home already has role-gated administrative entry points; moving them into shared menus requires capability and live-withdrawal equivalence. BoardScreen already distinguishes admin/edit/read actions from its returned access capabilities. INTERNAL surface admission alone is not equivalent to Board administration or strata governance authority.

## Required follow-up proof

- Inventory each route's actual current server capability/denial response before centralizing role menus; do not derive governance or acting capacity from Organization role numbers.
- Exercise Owner/Admin, Board Admin, Contributor, Viewer, Portal-only, multi-role and nonmember discovery/direct URLs, with live scope/role withdrawal.
- Verify independent deletion/departure/lifecycle receipts and public Board fallbacks after normal surface access ends.
- Test global/account → Organization → Board/Card → Back at all supported widths. Keep new domain slots with their canonical feature owners.
