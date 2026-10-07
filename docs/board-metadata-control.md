# Board metadata review and recovery

## Executed desktop and phone metadata recovery

Both unchanged `board-metadata.spec.ts` cases pass locally at 1280px/390px
against Production API, restricted schema-110 PostgreSQL, genuine scoped Worker
delivery and the production web bundle behind current Nginx/CSP. The client
recovers a deliberately lost committed save using the identical original
request/key, while another client receives the canonical name, description and
background. A real concurrent edit preserves the draft and disables saving
until explicit revision review; the next save uses a fresh key and reviewed
version. Keyboard/focus, 160-character name wrapping, multiline description,
fixed Purple surface, no horizontal overflow and persisted reload checks pass.

These two cases, both copy cases and both repaired lifecycle cases passed in
one six-case invocation in 4.2 minutes, including intentional rate-limit pacing.
Frozen API/Worker assemblies are mounted read-only in cached runtime images;
the web bundle is freshly built production output. Each Worker has only its
new fixture Organization scope, with discovery and identity mail disabled.
The browser phase allows unverified registered accounts. Current retained-image
execution, complete permission withdrawal/unchanged-state tests, stored-image
background acceptance, capacity and latency budgets remain required.

## Metadata contract

PRD-04 BOARD-FR-003/004 now has a MUI dialog on an editable active Board for its name, description and approved built-in background. Names follow the existing 1–160 character server bound. Whitespace-only descriptions become null. Keeping the current background omits both background fields, preserving the typed value, including a historical custom color or image reference. Explicit default/color choices use the approved Application contract.

The dialog reviews a canonical Board revision. A changed revision/content blocks saving until the current name, description and background category are reviewed again; the user's draft remains intact. The save sends only the reviewed revision and intended fields. An unconfirmed transport/malformed acknowledgment retains the original key, fields and version; other Board commands remain fenced until that request is recovered or current admission withdraws it. Canonical data advancing after a committed command does not replace the original retry.

The client validates acknowledgment scope, active state, incremented revision, name, description and expected background. It displays success only after that validation and refreshes the authoritative snapshot. Current scope/edit/lifecycle withdrawal aborts pending work and clears the review/draft; a late response cannot recreate success/recovery. HTTP admission denial uses fixed text without raw diagnostics. Dialog outcomes are announced and focus returns after closure.

Built-in rendering maps only the six stable names to fixed light/dark surfaces. Persisted CSS literals, URLs, image references and unknown historical values never enter styles; they use the default theme surface. Stored-image selection, ownership, Worker publication and rendering are implemented through the separate checked-image producer described in board-background-images.md; complete current-image acceptance remains unfinished. Complete native/realtime/concurrent/performance acceptance remains unfinished. Existing server updates still use the transactional authorization, idempotency, audit/event and Worker pipeline.

Client observations use the fixed board_metadata_update action through the existing bounded best-effort pipeline. They report review opens, use/retry, acknowledged success or failure duration, conflicts and unconfirmed exceptions. No scope IDs, names/descriptions, background references/selections, revisions, keys or diagnostics are retained. HTTP conflict/denial is not reported as a successful save, and late results after admission withdrawal are ignored. API parser source cases reject private extras atomically. These observations supplement server metrics and do not replace immutable business history or comprehensive acceptance evidence.

After observation integration, the focused metadata/archive suites have 28 passing cases. Actual report payload tests check same-key recovery over newer canonical data, private-field absence and conflict/denial outcomes. Eight API parser source cases reject Board IDs, content, background selection/reference, revision, key and diagnostic extras atomically. Web type checking/lint/build and full solution compilation with zero warnings/errors pass; API/native/PostgreSQL runtime evidence remains pending rigorous CI.

All 36 Board screen/drag cases pass; the full-screen metadata recovery case verifies that competing archive/create commands stay fenced and focus returns after receipt recovery. The Board workspace now exposes a named region, constrains long name/description wrapping and preserves description line breaks. Web type checking, lint and production build pass. These local checks do not prove native/API/PostgreSQL acceptance; rigorous CI runtime evidence remains pending.

The serial release browser suite includes board-metadata.spec.ts at 1280px and 390px, using the existing real scoped Worker and two live clients. Keyboard scenarios deliberately lose a committed save response, recover the identical request, reconcile a concurrent revision while preserving/reviewing the draft, verify a new request key after renewed review, and check canonical persisted name/description/color after reload. A 160-character unbroken name and multiline description check viewport overflow and fixed background/whitespace rendering. Browser TypeScript and discovery pass; actual native execution remains unverified until rigorous CI runs the immutable-image full suite. Production request limits, serial workers, zero retries and release gates remain unchanged.
