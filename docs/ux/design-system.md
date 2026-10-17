# Existing design system and shared compositions

UX-003 implementation baseline. React, MUI and Emotion remain the component/styling system. `appTheme.ts` remains the theme owner; `theme/designTokens.ts` names existing values without changing their appearance. No new styling dependency or component framework is introduced.

| Token / pattern | Existing value / owner | Use |
| --- | --- | --- |
| Primary / secondary | `#0f4c81` / `#3d6f8e` | MUI palette; text/icons/actions retain the existing contrast behavior |
| Canvas / surface | `#f5f7fa` / `#ffffff` | MUI background defaults and paper |
| Radius | 10 | Existing MUI shape; no mass restyling |
| Navigation width | 248px | Existing internal drawer |
| Content max width | 900px | Existing Notification page limit |
| Content / action gap | Theme spacing 2 / 1 | Shared header stack and wrapping action row; consumers may preserve their existing spacing |
| Typography | Existing Inter/system font stack, h4 weight 700 | Use semantic heading level separately from visual variant |
| Responsive navigation | Existing `sm` breakpoint | Mobile temporary drawer versus permanent desktop drawer; no new breakpoint system |
| Keyboard focus | Existing MuiButtonBase focus-visible outline | Preserve focus visibility and original button refs |
| Error / pending / success | Existing MUI Alert and status text | Owning feature decides messages, retry/consent and announcements |

## Shared library additions

`components/PageHeader.tsx` composes MUI Stack/Typography with explicit semantic heading level, title and optional description. Notifications and Global Card Search use it while retaining their existing h2 headings, visual variants and exact text. It owns no requests, permissions, forms or mutation state.

`components/ActionRow.tsx` provides a wrapping MUI Stack with flex gap. Organization discovery and Notifications use it. Existing Button/Link/TextField refs, accessible names, native form submission, disabled/pending state, pagination and original-command recovery remain owned by their features. Do not put automatic retries, authorization or consent into this layout component.

Organization settings now consumes the same PageHeader (retaining its h1) and ActionRow for save and explicit draft-review choices. Its field error mapping/focus stays in the feature: the existing name validation and server logo refusal supply static linked help; page-level error/support references, drafts and original-command recovery remain intact. This follow-up requires its own current-source evidence.

Before extracting another pattern, identify actual duplication and at least two real consumers. Prefer MUI controls directly where a wrapper adds no useful shared behavior. Form validation, canonical read admission, error support references and destructive confirmation remain in the working feature modules until a separately tested migration justifies extraction.

## Responsive safeguards added

The fixed shell header wraps controls and observes its actual height. Main content and permanent-drawer offsets follow that measurement, including user text zoom, rather than assuming every header stays 56/64px tall. The observer disconnects on unmount; it adds no network polling.

Shared MUI alerts wrap message and recovery-action rows. Complete text stays readable instead of becoming an unfocusable scroll region; the feature still owns its original retry callback and consent state.

## Safeguards retained

- Long text wraps through existing Typography/CardContent/Button overrides; do not truncate canonical names to conceal overflow.
- Shared autofill keyframes and input detection remain in appTheme/CssBaseline for production CSP compatibility.
- Existing label and button transitions avoid interpolating exempt disabled colors into enabled low-contrast states.
- Portal and internal surfaces retain separate admission. A page composition cannot create a new authorization boundary.
- Preserve semantic fields, table headings, explicit dialog labels/focus and non-color status. Component snapshots alone cannot establish accessibility.

## Fixtures and review

Component tests cover long content/headings, disabled → enabled state, preserved input draft, action refs/focus and native submit ownership. Existing migrated-consumer tests remain required. `ux-foundation.spec.ts` exercises actual Organization/Search/Notification routes with controlled responses and transport; live service tests separately check permissions and persistence.

Review loading, empty, error, disabled, focused and long-content states at 320/390/768/1280px and text zoom. Production Nginx/CSP checks must serve current built assets. Screenshots are review material until a product/UX reviewer explicitly approves a deterministic baseline; never bless new screenshots automatically or substitute Axe for manual assistive-technology acceptance.
