# Organization configuration — PRD-27

This describes configuration under implementation for [PRD-27](https://github.com/nickmailhot-lang/StrataAI2/issues/28). Estimated work remaining: **29%**. Configuration validation/storage, private replay/Activity, MUI review/history/recovery and operational lifecycle workflows have scoped evidence. Local restricted Production checks cover independent Portal access, archived-record/configuration-history preservation through Worker terminal deletion, session expiry, HTTP logout ordering and separate Worker delivery/restart. Maintained desktop/phone tests also verify distinct Owner/Admin configuration attribution, draft and keyboard-review recovery, independent logout and accessibility scans in Demo mode. The comparison region accepts keyboard focus and scrolling. Fixed configuration client observations now have component, API and native original-retry/privacy evidence. Complete current-source regressions, remaining retention/reference and data/permission consistency, full UX/observability and exact-image release acceptance remain required. Earlier sections record historical increments; their percentages and pending items do not describe the current release state. See the [complete acceptance map](prd-27-acceptance.md).

## Reviewed configuration and historical meaning

For support triage, see [Organization lifecycle support](organization-lifecycle-support.md):
uncertain acknowledgment handling, current error codes, separate Worker recovery,
replay/parent version interpretation and historical-access boundaries. This guide
documents implemented behavior; telemetry ingestion and complete release acceptance
remain required.

`OrganizationConfigurationData` requires a legal name, jurisdiction and IANA timezone. Legal name is distinct from the canonical Organization display name. No configuration is fabricated from an existing tenant's name, type or migration time. An authorized, unconfigured read returns version zero and a null revision.

Optional values include corporation/registration identifier, civic address, lot count, paired fiscal year-end month/day, AGM and depreciation-report cycle in months, insurance renewal date, management-company name, emergency contacts, default categories/priorities, intake Board/List and jurisdiction policies. Policies contain key, value, source and optional notes. Configuration records reviewed values and does not confer legal compliance or imply local-law deadlines.

Validation rejects unknown JSON properties, control characters, unsupported timezones, invalid calendar dates, excessive payloads/collections, duplicate category/priority or policy keys, and emergency contacts without a telephone or valid plain email address. The maximum configuration payload is 65,536 bytes; each collection is limited to 32 items. Errors identify public field names without echoing private input. A valid field value never authorizes a relationship ID.

Each revision preserves configuration, Organization ID, revision version, the server's parent display-name/type/version snapshot, authenticated actor, event ID, correlation ID, original creation time and actual update time. Changing management company, jurisdiction or timezone preserves older values and timestamps in history. Historical parent snapshots do not replace the current canonical Organization record.

## Commands, permissions and recovery

The Application service enters the existing Organization unit of work. It requires a current active account, active Organization and active Owner/Admin membership before private reads, history, validation responses or receipt recovery. It checks admission again before success; transaction failure rolls back all participating state. Governance titles and Organization classification do not confer configuration authority. No nested Organization transaction is introduced.

The service freezes nested input before awaiting admission/locks. A change requires the expected configuration version and a nonempty idempotency key. The SHA-256 intent fingerprint binds Organization ID, expected version and the complete reviewed configuration. Receipts additionally bind the actor and key. A matching retry within 24 hours returns the original acknowledgment, including after a later successful edit. Changed intent conflicts; expired keys remain reserved and cannot execute again. An unrelated account or a demoted administrator cannot recover a protected receipt.

The browser now retains one original reviewed request per account/Organization in tab-scoped session storage before sending it. Recovery preserves the exact canonical request bytes, original version, account and retry key; it does not rebase to a later GET or execute automatically. A reload/remount restores protected values only after current configuration admission and shows an explicit original-submission retry. A validated original acknowledgment or definitive server refusal removes only that matching saved request; a newer GET, transient failure or navigation does not. A failed write/readback prevents the first mutation. Invalid records, mismatched completions and attempts to replace an unresolved original are refused without silently deleting it. Recovery is bounded to 32 configuration scopes and does not evict existing originals for capacity. Server receipt expiry remains authoritative; there is no browser timer that silently reuses an expired retry key.

Acknowledged sign-out or account deactivation clears only configuration recovery entries alongside the existing invitation cleanup, preserving unrelated site data. A newly admitted account clears foreign configuration recovery keys before restoring its own scope; current live configuration withdrawal clears the known account/Organization entry. The store is per-tab browser recovery, not an audit source, backup or legal retention policy. Unsubmitted invalid drafts and complete permission-loss/degraded-browser recovery acceptance still require review.

Intake selection resolves the actual Board and List inside the owning transaction. Both must belong to this Organization, the List must belong to the selected Board, and both must be active. Current Organization Owner/Admin admission grants Board administration under the existing canonical Board policy, including private Boards. Another tenant's Board, a sibling Board's List, or an archived intake target is rejected without disclosing its details. Existing revisions preserve historical relationship IDs after operational archive.

History uses descending revision seek: the store reads at most 51 rows, the service returns at most 50 and supplies the last returned revision as the next exclusive boundary. Invalid cursors are rejected after private admission. History reads do not replay mutations.

Intake List discovery now has a separate private endpoint: `GET /organizations/{id}/configuration/intake-boards/{boardId}/lists?afterRank={rank}`. It returns `{ organizationId, board, items, nextAfterRank }`; Board/List projections contain only identity, name, version and List rank. The store reads at most 51 active owning Lists; the service returns at most 50 and uses the final returned 30-digit rank as the exclusive next boundary. Empty pages have no continuation. The existing tenant/Board/rank index supports this seek. Production uses the owning restricted transaction and shared Board/List locks, without loading Cards or opening a foreign tenant session. Current configuration-administrator admission precedes cursor validation and is checked again before disclosure. Account binding and private/no-store caching match the other configuration routes. Duplicate, empty and malformed boundaries are rejected with `invalid_configuration_intake_cursor`. The MUI client uses this endpoint with active-account and configuration-administrator checks before and after each page. It refuses foreign, malformed, oversized, unordered, duplicate or nonexclusive source pages and unexpected fields. The picker replaces prior List options on continuation, retaining at most one already verified selected List; a Board change or discovery restart clears the List cursor and verification. More intake Lists is an explicit action. Review requires the selected destination to have been verified, and the server revalidates the relationship during the eventual mutation.

## Storage and remaining Production requirements

The Demo store serializes current state, revisions and receipts so caller/reader collection mutations cannot rewrite history. It participates in Organization rollback alongside existing Organization and Work stores. Current state, historical revision, body-free event source, event-ID reservation, original receipt and jurisdiction/identifier namespace reservation are atomic. Registration conflicts return a non-disclosing result. This is an in-memory Demo boundary; it is not PostgreSQL evidence.

Migration `137_organization_configuration` and the PostgreSQL store implement tenant-owned current configuration, immutable revisions, original actor/key receipts and a body-free event source. All four tables use forced RLS and restrictive Organization references. Current configuration is one row per Organization; revisions/events use the Organization/version key; receipts use Organization/actor/key with one original receipt per revision. The primary keys support bounded version seek. Intake Board/List references on both current and historical rows preserve tenant and relationship identity with restrictive foreign keys. Registration uniqueness uses the normalized jurisdiction/identifier namespace and returns a non-disclosing conflict.

The namespace trims the .NET whitespace set and uses simple invariant uppercase, without Unicode normalization or case expansions. SQL uses an explicit `translate` mapping of 1,449 scalar pairs rather than locale-dependent `upper`. The complete mapping generated by the adopted Windows and Linux .NET runtimes agrees. The mandatory restricted persistence contract compares every runtime case pair and representative mapped/unmapped/whitespace cases with the SQL function. Runtime upgrades must retain this contract; any intentional namespace change requires a new migration and collision/compatibility review, rather than editing an already-applied migration.

| Entity/source | Identity and ownership | Fields, references and lifecycle |
| --- | --- | --- |
| Current `OrganizationConfiguration` | Primary key `tenant_id`; exactly zero or one current row per Organization. | Positive configuration version and immutable server-generated revision JSON; required legal name/jurisdiction/timezone and optional fields described above. Generated jurisdiction/identifier and intake references support uniqueness and tenant-safe restrictive Board/List FKs. Current writes require version compare-and-swap, active Owner/Admin and current parent snapshot. |
| Historical configuration revision | Primary key `(tenant_id, version)`; one retained revision per successful change. | Required actor references `users`; unique event identity references retained audit. Current and historical intake FKs preserve Board/List relationship identity. Updates/deletes are refused; history is private to current authorized administrators. Primary-key ordering supports descending bounded history. Retention/deactivation and operational deletion interactions still require full acceptance. |
| `JurisdictionPolicy` | Embedded value identified by `(Organization, configuration revision, case-insensitive ASCII policy key)`. | Required key/value/source, optional notes; at most 32 per revision. Keys are unique within a revision. No separate policy table, independent lifecycle or unbounded policy query is introduced. A policy edit creates a new configuration revision; older values/source/notes retain historical meaning. Confidentiality, permission and retention follow the enclosing revision. |
| Original change receipt | Primary key `(tenant_id, actor_id, key_id)`; unique `(tenant_id, version)` binds one receipt to its historical revision. | Required reviewed fingerprint, immutable original result and expiry; actor/user and revision references restrict deletion. Expiry is 24 hours for replay, not automatic key reuse or historical deletion. No update/delete capability is granted. |
| Body-free event source | Primary key `(tenant_id, version)`; unique audit event ID. | Safe event/actor/entity/version/correlation/source-time fields and restrictive history/audit references. Source creation and reference-only durable delivery publication are atomic with the private revision. First Worker readiness has retained clocks and lease fences; Activity and authorized SignalR replay remain required. |

Deferred current-to-history and history-to-receipt/audit references prevent a partial revision from committing. The history source must exactly match the retained current row; its private trigger creates an append-only audit and body-free event with the same identity, actor, version, correlation and source time. Receipts must match the immutable revision. API grants permit current writes and history/receipt insertion; direct event writes, history/receipt rewrites and destructive configuration cleanup are withheld. Migration 138 adds restricted configuration delivery and first-publication clocks; Activity and authorized replay remain unfinished. Runtime readiness requires migrations 137 and 138 for both API and Worker. Production binds the PostgreSQL store and does not use a Demo fallback.

The remaining data review must cover the complete optional-field/schema contract, historical retention and Organization lifecycle transitions, including operational deletion of referenced intake records. Unicode namespace parity is now checked across the adopted runtimes and restricted SQL; future runtime/namespace changes retain the migration/compatibility obligation above. Separated portal behavior and deployed error/recovery behavior still require verification.

## HTTP contract

`GET /organizations/{id}/configuration` returns `{ organizationId, version, revision }`; an unconfigured authorized Organization returns version zero and a null revision. `PATCH` at the same path accepts `{ version, configuration }` with an `Idempotency-Key` header containing a nonempty UUID. The configuration uses camel-case versions of the named fields above. Both envelope fields are required. Unknown and duplicate JSON properties are rejected, including in nested policy/contact records. The request envelope is bounded to 98,304 bytes, including chunked requests; Domain validation additionally bounds the reviewed configuration to 65,536 bytes.

`GET /organizations/{id}/configuration/history?beforeVersion={revision}` returns `{ organizationId, items, nextBeforeVersion }`, with at most 50 descending revisions. The next cursor is exclusive. All responses use private/no-store caching. Browser mutations retain the existing `X-StrataAI-Request: 1` request requirement; `X-StrataAI-Expected-Actor` optionally binds a request to the account that reviewed the private record. Current active Owner/Admin admission precedes cursor, body and retry-key validation, and the command checks admission again inside its owning transaction. Foreign or unauthorized targets return a safe not-found result without exposing private values.

Validation uses a stable Problem Details `code`: `invalid_configuration_request`, `invalid_configuration_cursor`, `invalid_configuration_{Field}`, `configuration_intake_unavailable` and `idempotency_key_required` return 400; stale revisions, changed/expired retry keys and unavailable registration identifiers return 409. A lost database acknowledgment or unavailable source returns 503. Authentication/account binding failures return 401. Errors do not echo private field values. Operator telemetry labels contain fixed operations/outcomes/error categories and a keyed-attempt flag, without tenant/actor IDs or configuration values.

The safe event source contains event/Organization/actor IDs, entity type/ID, revision, correlation and actual source time. It excludes legal name, address, contacts, identifier and policy values. PostgreSQL persists the required `ORGANIZATION_CONFIGURATION_CHANGED` audit/source atomically. Reference-only durable publication and separate Worker readiness now have focused restricted-runtime evidence. Activity, authorized SignalR recovery and lifecycle behavior remain unfinished; readiness does not confer recipient authorization.

## Executed evidence and limits

The bounded intake increment passed locked API and persistence-contract builds with zero warnings/errors. The expanded Linux source-host run passed **41/41 PRD-27 executions**, including seven new executions for 50-plus-3 List paging, exclusive/exhausted boundaries, cursor admission, final account-admission loss, HTTP private caching, reviewed-account substitution, foreign Boards and archive refusal. The expanded real restricted PostgreSQL C# contract passed the same 53-List pagination, exhaustion, foreign-tenant and archived-Board checks together with the original configuration persistence/receipt/history/rollback/namespace contract and all 137 readiness entries. Owned test containers, database, runtime roles and credential files were removed afterward. These results establish source-host and restricted-store behavior; browser use of the new route, current exact-image CI, and full PRD acceptance remain required.

The locked API build passed with zero warnings/errors. The focused Linux PRD-27 run passed **24 distinct executions**, including **10 configuration cases** and the existing classification cases. Configuration cases cover original receipts after management/timezone/jurisdiction edits, denial after administrator demotion, reserved expired keys, stale/changed intent, bounded history, final-admission rollback, actual intake substitution/archive, deep snapshots, registration conflicts and rollback of state/history/events/receipts/registration reservations. The backend/test source manifest matched that run. These tests exercise Application commands and Demo transactions; they do not prove HTTP, Production, accessibility, realtime or full PRD completion.

The restricted-login C# PostgreSQL contract passed configuration creation, historical management/timezone/jurisdiction changes, original receipt recovery, changed/stale intent, reserved expired keys, registration conflict, access withdrawal and final-session rollback of current/history/audit/events/receipt/registration reservations. Account/session admission is synthetic in this executable; it is not HTTP acceptance. Both restricted API and Worker factories individually rejected/restored all 137 migration entries. The complete original migration runner passed repeat, populated forward upgrades, serialization, failure rollback and unrecorded-migration refusal, plus new restricted-role configuration isolation, immutability and atomicity assertions. These results include historical intake foreign keys and exact original source/audit linkage.

The expanded restricted PostgreSQL contract reproduced a foreign intake lookup attempting to open another tenant session inside the owning command. The store now resolves intake directly within the owning tenant, with shared locks on the selected Board/List. The expanded contract passed after that fix, including stable non-disclosing refusal of the foreign Board and all 137 readiness entries.

A separate 15-case namespace experiment found four disagreements between the original locale-dependent SQL conversion and Linux .NET invariant conversion. After the explicit SQL mapping change, the expanded restricted C# contract passed every runtime scalar pair and its mapped/unmapped/whitespace matrix, together with the complete configuration persistence/recovery/rollback contract and all 137 readiness entries. The complete original migration runner passed again against the corrected schema, including its existing upgrade/serialization/rollback phases and new configuration assertions.

The HTTP-focused Linux run passed **34 distinct PRD-27 executions**, including **10 HTTP executions** covering persisted refresh/history, original retries after later changes, stale-write conflict, missing/unknown/duplicate schema fields, invalid field input, bounded request size, required retry keys, foreign targets and substituted reviewed accounts. The source snapshot includes the complete root build marker. These source-host HTTP tests use Demo persistence; the separate restricted PostgreSQL contract supplies persistence evidence, not deployed HTTP evidence.

The new Production HTTP acceptance scenario passed on its first attempt against the current compiled API, restricted PostgreSQL and corrected migration 137, using actual authenticated sessions with verified-email enforcement. It exercised empty/read/change/refresh/history, fiscal/cycle/insurance/contact/category/policy fields, original acknowledgment after later management/jurisdiction/timezone changes, stale/schema refusal, registration collision and guessed foreign targets. Source hashes matched; its owned database, containers and credential files were verified absent afterward. This is local Production HTTP evidence, distinct from the source-host tests and the still-required complete immutable-image, UI, lifecycle and realtime acceptance.

The earlier complete Linux Domain run passed 801 cases, retaining the original 787 cases and adding 14 configuration validation cases. Its Domain source is unchanged; it predates the Application/store additions and is scoped to Domain behavior. The first unfiltered API run executed 764 cases: 763 passed and the unchanged architecture assertion failed because its isolated source copy omitted the root build marker. Against the corrected complete source copy, the unfiltered Production-store/readiness API run passed all **764 cases**. That run predates the intake fix and new HTTP routes. The latest unfiltered API run passed **774/774 cases**, with the current API/Application/Domain/Infrastructure/API-test source hashes matching. Its snapshot predates only the separately verified namespace SQL and persistence-contract expansion. The complete frontend suite passed **2,089/2,089 cases across 142 files** with two test workers and unchanged assertions/deadlines. Current immutable-image results and the remaining full PRD obligations must still be reviewed before closure.

## Browser response boundary

The configuration browser model admits only the requested Organization, matching positive current/revision versions or the explicit version-zero/null empty state, supported Organization types, valid source identities and exact UTC timestamps. History admission requires at most 50 descending unique versions/events and an exclusive next boundary matching the last returned revision. Unsafe JavaScript integers are refused rather than rounded into another revision.

Every named configuration field has a bounded response shape, including nested contacts and sourced jurisdiction policies. Unknown properties, malformed dates, unsupported timezones and invalid intake relationship shapes are refused with a fixed unavailable error that excludes private values. Returned nested values are copied so transport-object mutation cannot rewrite review state. Server-side business validation and actual tenant/Board/List admission remain authoritative; browser Unicode full casing does not replace the pinned server's ordinal duplicate rules.

The dedicated browser-boundary suite passed **37/37 cases**; frontend type checking and lint passed. This increment is preparation for the MUI form/review/history workflow. It does not provide a rendered configuration screen, authenticated transport recovery, realtime delivery or complete PRD acceptance. Remaining work estimate stays **75%**.

## Account-bound browser transport

Configuration reads and each history page check the active `/me` profile before and after retrieval. Configuration requests send `X-StrataAI-Expected-Actor`; a changed account refuses the result before it becomes review state. Requested tenant IDs and history versions are validated before dispatch. The shared same-origin API transport retains cookie and CSRF protections, and bounded operations reject hung or cancelled responses before a later request can start.

A reviewed change stores immutable serialized configuration, expected version, Organization, actor and retry key. Caller mutations and mutations of returned review copies cannot change its bytes. Uncertain delivery retains that exact intent for retry. An acknowledgment must match its Organization, actor, next version and reviewed values; a mismatched response cannot establish success. The original acknowledgment can legitimately describe an older revision after another successful change, so the page must perform a fresh authorized read before showing current state. A newer read does not resolve an uncertain original submission. No automatic mutation retries are introduced.

The common Problem boundary admits only fixed configuration codes at their declared HTTP status, including specific actionable field names. Arbitrary field suffixes, server titles/details and rejected private values are discarded. Account withdrawal must clear protected page state; network failure must preserve the draft and original intent. The MUI page increment below now implements those in-page transitions and rendered review; navigation/reload recovery and configuration realtime withdrawal still require implementation and acceptance.

The combined configuration response/transport/shared Problem test run passed **73/73 cases**. Frontend type checking and lint passed. These source tests establish the browser model and transport behavior, not a rendered end-to-end configuration workflow. Estimated PRD-27 work remaining remains **75%**.

## MUI configuration workflow increment

Authorized Organization home navigation opens `/app/{organizationId}/configuration`. The page provides every named legal, strata and operational field, optional collections of emergency contacts/categories/priorities, and jurisdiction policies with explicit value/source/notes. Unconfigured legal name, jurisdiction and Organization timezone start blank rather than being inferred from display or account settings. Invalid/incomplete numeric, fiscal, contact and policy drafts are retained for correction.

Intake Board choices use the bounded owning Organization directory. Actual active Lists use the separate bounded configuration intake endpoint, retaining no Card payloads. Configuration administrator authority is checked before and after intake reads, independently of ordinary Board membership. Directory pages replace the prior choices while retaining a verified selected Board. List pages replace unrelated prior options while retaining the verified selected List. Temporary discovery failure preserves the legal draft and exposes retry. Source-host checks cover continuation to a later List and approval of that destination; complete production browser pagination and lifecycle/permission/degraded-state acceptance remain required.

Review performs a fresh account-bound current-state read and presents current and proposed values together. Approval uses that visible version and an immutable reviewed intent. A conflict preserves the draft and requires a new current-state check/review; it does not silently retry a rebased change. An uncertain submission disables editing and exposes only an explicit original-intent retry. Newer current reads retain that unresolved intent. After the original receipt is recovered, another admitted read populates current state, including when a later revision has superseded the original result.

History uses descending version pages and retains the revision's historical Organization name/type/version, actor reference, actual source timestamp and approved public support reference. Private values are excluded after denied admission. Existing Organization metadata invalidation hides private page values while authority is rechecked, queues checks behind active operations and preserves unresolved work until admission is confirmed or definitively denied. This does not deliver configuration-specific changes from another administrator: the required restricted Worker/journal/SignalR source and reconnect recovery are still unfinished.

The review Dialog uses the safe return action for initial focus. Source tests cover explicit approval, focus restoration, uncertain original receipt recovery after a later read, conflict review, actual intake selection and private draft/history withdrawal. Added desktop/phone browser scenarios cover every field, owning intake, keyboard approval, refresh and history. They are collected into mandatory browser coverage but have not yet been executed for this increment. The earlier focused run passed 138 configuration/profile cases before the final intake-retry addition; final source results and immutable-image/deployed acceptance must be recorded separately. Browser fixture type checking passed and the complete partition verifier retained all 352 cases in 133 intact files, with no original timeout/assertion/retry changes.

After the intake-retry addition, the final focused configuration/profile run passed **139/139 cases across seven files**, with frontend type checking and lint also passing. The 12 HTTP architecture/browser-partition verifier cases passed, as did browser fixture type checking and complete 352-case collection. An unfiltered frontend source run was already live when the final intake-retry change was added; it is retained for its actual scope and cannot establish the complete current source result. Current complete source, build-once images and deployed browser results remain separate acceptance obligations.

## Executed browser acceptance and lifecycle wire correction

The first local desktop/phone execution failed both cases at intake List selection. Retained HTTP traces showed valid owning Board/List identities, current administration access and lifecycle values `active`; the browser incorrectly required the SQL/domain spelling `ACTIVE`. The client and its new fixtures now follow the actual lowercase JSON contract. Unsupported uppercase, archived and unknown states remain refused rather than being coerced into active records. The focused client/page run passed **45/45 cases**, with frontend type checking, lint and the corrected production web build passing.

With that source correction, both original desktop/phone scenarios passed their first attempt, with **zero retries** and unchanged 30-second deadlines and assertions. They used real verified accounts, the compiled Production API, restricted PostgreSQL with migration 137, the current web build and the scoped separate-Worker fixture. The scenarios exercised every named field, actual owning intake selection, explicit keyboard review/approval, authoritative persistence, refresh, source policy notes and history focus. Frontend, compiled API source and browser-case hashes matched their recorded manifests. Owned API/web/Worker containers, database and credential files were independently verified absent afterward. The Worker fixture invocation does not prove configuration publication: the required configuration event/Activity/SignalR path remains unfinished, and no Worker readiness/publication result is inferred from UI success.

The earlier unfiltered source run passed **2,176/2,176 cases in 146 files** before the final intake-retry addition. A later run overlapped the wire-contract edits, mixed cached pre-fix client code with the updated fixtures and failed six client checks; that failure remains recorded. Both runs are terminal and are scoped to their actual sources. The fresh complete run with the corrected frontend frozen by manifest passed **2,181/2,181 cases across 146 files**, with zero source-hash mismatches across 301 frontend files. Current exact-image CI remains required before release acceptance. Browser fixture type checking and complete 352-case partition verification also passed after the scoped Worker fixture was added.

Navigation/reload retention of unresolved submissions, full telemetry, complete keyboard/screen-reader/mobile/degraded/performance proof, Demo authoritative Activity/Audit parity, publication/replay, suspension/deactivation and remaining schema/retention/portal/intake review are required before closure. Estimated PRD-27 work remaining is **65%**.

Related: [Organization Types](organization-types.md), [Organization transactions](organization-command-transactions.md), [schema upgrades](schema-upgrades.md), [documentation index](../README.md).

The paged browser increment passed **65/65 focused client and screen executions**, frontend type checking, lint and a production web build. New checks cover private scoped requests, 50-row continuation/exhaustion, malformed/foreign/unordered/oversized pages, nonexclusive cursors, account replacement, and selecting/approving a later-page List while keeping only the selected prior option. Both unchanged Production desktop/phone scenarios passed on their first attempt with zero retries, the original assertions and 30-second deadlines, using the compiled paged API and restricted PostgreSQL with migration 137. Frontend/browser source hashes matched; owned API/web/Worker containers, database and credentials were cleaned up. This proves the existing one-List happy path through the new route, not production browser continuation across more than 50 Lists or the remaining full acceptance branches. The complete frozen current frontend suite passed **2,201/2,201 tests across 147 files**, with zero mismatches across its 303-file frontend/browser manifest. At that increment the unfiltered API suite was still running; its subsequent 781/781 terminal result is recorded below. Exact-image CI remains required.

Production pagination acceptance now includes `organization-configuration-intake-pages.spec.ts` at desktop and phone widths. Each scenario creates 53 Lists through the real API, verifies the 50-plus-3 exclusive pages and all 53 distinct identities, retains a selected prior List while discarding unrelated prior choices, approves a later-page destination by keyboard, verifies authoritative persistence and refresh, and asserts that the screen does not request a complete Board snapshot. The final source passed **2/2 scenarios with zero retries**, unchanged 30-second deadlines and unchanged pagination/approval assertions. Browser type checking and the complete **354-case/134-file partition** passed; all four shards retain complete file ownership (89/89/88/88). Frontend/browser-case hashes matched, and owned API/web/Worker containers, database and credential files were removed.

Earlier executions remain recorded: the first and Board-delivery-only fixtures each passed the phone case but timed out in desktop Board selection. Retained traces showed an owning directory result and private-authority rechecking. A fixture that required a historical metadata event then failed both cases: an initial stream may instead return an empty page or reset. The final fixture waits for the actual initial Organization metadata page, any required authority recheck, and canonical Board delivery before opening the picker. It adds no automatic retry or longer deadline. These happy-path and pagination results do not establish permission-loss/reconnect/degraded-state, navigation/reload original-intent recovery, complete accessibility/performance/telemetry, configuration Worker delivery, suspension/deactivation or full PRD acceptance.

The original-submission recovery increment passed **98/98 focused executions** across configuration transport/recovery/screen and profile sign-out/deactivation files, plus frontend type checking, lint and production web build. New checks cover exact bytes/version/key across remount against a newer revision, no automatic send, failed-storage mutation prevention, immutable unresolved originals, malformed/cross-scope records, bounded storage, and acknowledgment-only sign-out/deactivation cleanup. Browser fixture type checking and complete partition verification passed **356 cases across 135 intact files**, 89/89/90/88 across four shards. The complete recovery frontend run passed **2,213/2,213 tests across 148 files** before the final authority-readiness marker was added. The current marker source passed the same **98/98 focused executions**, type checking/lint/build and the Production run below; the subsequent complete run against that frozen final source passed 2,213/2,213 across 148 files in 562.67 seconds with zero mismatches across the 307-file frontend/browser manifest. The pagination request guard now checks both root and `/api` Board snapshot paths, matching the actual same-origin transport.

The final Production recovery run passed **6/6 configuration browser scenarios with zero retries**, preserving all original assertions and 30-second deadlines. These include both original full-field review/history cases, both 53-List continuation cases, and both new lost-acknowledgment navigation/reload cases. The recovery scenarios make the server commit before withholding the response, advance configuration through another real API edit, navigate away and reload without automatically resending, then explicitly retry the exact original bytes/key and display the later authoritative revision. Current frontend/browser hashes matched all 307 recorded files. Owned API/web/Worker containers, database and credential files were removed.

The first recovery run passed five of six cases: desktop keyboard retry never reached the server after reload's initial private-authority recheck interrupted the action. A revised fixture then passed five of six because it waited for a transient authority caption that a restored-submission notice correctly replaced. Both failures remain retained. The final readiness check observes actual metadata delivery, the resulting authority-check generation and its completed state before exercising the same keyboard retry. The readiness marker contains no account, Organization or configuration values and does not confer authorization.

The unfiltered Linux API suite is now terminal and passed **781/781 executions**, with zero source-hash mismatches across 637 API/Application/Domain/Infrastructure/API-test files. This includes the new bounded intake endpoint and all original host assertions. Current exact-image build-once CI and the remaining full PRD obligations are still required. The final complete frontend run passed 2,213/2,213 across 148 files in 562.67 seconds with zero mismatches across its 307-file frontend/browser source manifest. Estimated work remaining is **65%**; this issue remains open with no approved deferrals.


## Configuration delivery acceptance boundary

Configuration reads and changes require a currently active, verified Owner/Admin under `OrganizationConfigurationService`. The existing Organization metadata synchronization admits active membership, and its client validator supports Organization/membership/invitation events. Its delivery results therefore do not prove configuration delivery. Adding configuration sources to that stream without configuration-specific recipient admission would broaden the private configuration audience.

The remaining delivery implementation must retain the committed configuration event identity, actor, revision, correlation and source time, publish body-free safe metadata, enforce configuration eligibility before and after replay/delivery, and bind recovery to the current account and membership authority. Worker lease/source checks, transactional Activity/Audit/outbox behavior, Demo rollback/audit parity and opaque missed-event recovery must be verified for configuration. Required negative cases include an ordinary member, a foreign Organization, withdrawn Owner/Admin authority, deactivated account, changed membership, replayed/expired cursor, and stale Worker lease. Disconnect/reconnect and missed-event recovery must preserve unresolved original submissions while refreshing authoritative configuration. These are remaining acceptance requirements, not claims about the current metadata stream.


## Canonical configuration deep links

Configuration route IDs and the shared internal/portal admission boundary now use lowercase UUID scope consistently. Previously HTTP returned canonical IDs while realtime and admission compared them with raw uppercase route text. The configuration-page regression reproduced the mismatch; the Production desktop/phone run then exposed the outer admission mismatch before the page could load. Canonicalization preserves the current surface check and does not turn an internal grant into a portal grant.

The complete fix passed **116/116 focused cases across six files**, frontend type checking, lint and production build. Two new admission cases cover uppercase UUIDs on both surfaces and refusal of a response for the other surface. Six Production desktop/phone browser cases passed on their first attempts with no retries: bounded intake pagination, lost-acknowledgment recovery entered through uppercase deep links, and full-field keyboard/refresh/history. Lowercase return links, later lowercase navigation, exact original request bytes/key after a newer revision, and no automatic resubmission remain asserted. All original assertions and deadlines remain intact. The failed first Production run (four passed, two uppercase entries refused by outer admission) and regression failures are retained; owned fixture containers/database and credential files were cleaned.

Before applying the outer admission change, an unfiltered source run passed **2,214/2,214 across 148 files** in 628.20 seconds with zero mismatches across its 308-file frontend/browser manifest. The fixed admission source was tested in an isolated copy while that run remained live; all 304 frontend source files then matched the worktree exactly. A fresh full suite covers the final combined source. Neither the earlier full run nor the six local browser cases establishes immutable-image or complete PRD acceptance. Estimated remaining work stays **65%**.


## Durable configuration Worker delivery

Migration **138** retains every committed configuration source and adds actor/correlation/source-time columns plus first `ready_at` and a generated `updated_at`. The immutable source payload is derived from its original history/audit; upgrade time never replaces source time. API and Worker readiness require the new ledger entry. A historical nonempty upgrade, repeated migration application, failure rollback and runner serialization are exercised by the mandatory migration checks.

Each source atomically publishes `ORGANIZATION_CONFIGURATION_EVENT_READY` under `organization-configuration-delivery`, carrying only its event UUID. The first job keeps the committed actor and correlation reference. Configuration's existing 256-character correlation contract is preserved for this specific type/service pair; other producers retain their 120-character job limit. Runtime roles cannot directly update configuration events, and Worker cannot read private configuration history. Its narrow delivery capability checks owning tenant, canonical job/source identity, actor/correlation, running Worker/lease and expiry before publication and again after it. Duplicate acknowledgment preserves the first publication time; a reclaimed job keeps its original identity. Historical actor departure and later configuration revisions do not strand an already committed source.

Production discovery is enabled by default and can be disabled with `STRATAAI_ORGANIZATION_CONFIGURATION_DISCOVERY_ENABLED=false`. It seeks through indexed current Organization rows, returns at most 100 route IDs, and uses a configuration-only invoker claim under tenant RLS. Mixed typed claim modes are rejected. Each Organization pass processes at most 32 jobs or 250 ms, then advances and wraps; unrelated provider queues are neither claimed nor retired. Explicit tenant execution also registers this handler. Demo refuses enabled discovery, and invalid settings fail startup. Existing Worker image configuration checks include these refusals.

The delivery handler passed **17 focused cases** and the full compiled-source Linux Domain suite passed **818/818**. Builds for Domain, persistence contracts, API tests and Worker passed without warnings. Restricted PostgreSQL checks passed source publication, 256-character correlation, all **138** API/Worker readiness entries, wrong tenant/actor/event/Worker/lease refusals, API capability denial, late expiry rollback, duplicate and reclaimed delivery, source/readiness clocks, private-history access denial, historical actor departure and untouched unrelated provider work. The complete migration runner also passed against a retained historical source. A separate Production Worker process with no configured Organization IDs discovered pending source jobs, completed them, preserved original payload/first clocks and left unrelated jobs pending. All owned fixture containers, databases, roles and credential files were removed.

The initial discovery contract exposed an ambiguous unqualified SQL column; its failed run is retained, and discovery now routes through one indexed current row per Organization. An initial Windows Domain execution retained 16 failures involving local socket binding and timezone behavior; the unmodified Linux suite passed all 818. A source snapshot preparation attempt exceeded a Windows path limit before launching fixtures; the corrected shorter owned path was used for upgrade proof. These failures do not replace or weaken original assertions/deadlines.

The separate unfiltered Linux API run completed **781/781**, with no failed,
skipped, timed-out or aborted cases. All **641** recorded backend/API-test source
files matched the worktree after completion. Its owned test container was
removed and existing services were preserved. The delivery commit's PostgreSQL
job in [CI run 38068687893](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38068687893)
also passed. These results establish their source/persistence scopes, not
configuration recipient replay, complete PRD acceptance or a green image gate.

The final canonical-deep-link frontend suite subsequently passed **2,216/2,216 across 148 files**, with zero mismatches across its 308-file frontend/browser manifest. The new delivery C# source has a separate unfiltered API run; immutable-image CI and complete PRD acceptance remain required. Estimated work remaining is **60%**. Configuration-specific authorized replay/SignalR and Activity, Demo authoritative audit parity, suspension/deactivation with historical access, remaining schema/retention/portal consistency, unsent/degraded/permission-loss branches, full accessibility/performance/telemetry and operator acceptance stay in scope. Worker readiness is a publication fact, not a grant to an active member.

## Configuration replay window

The Application layer now has a separate, pure configuration event-window
builder. Configuration versions supply its tenant sequence; it does not borrow
the all-member Organization metadata stream or audience. A current Owner/Admin
read scope is still required from the future owning reader/coordinator. This
builder grants no read authority and has no HTTP or SignalR registration.

Only a contiguous ready prefix advances the cursor position. A pending revision
blocks later publication, including page lookahead; missing, reordered, duplicate
or future history resets the entire window. Foreign tenant sources, invalid
source identity/correlation and a delivery time preceding the source time also
reset. Pages are bounded to 100 with one lookahead row. The original body-free
source event and its source clock are retained separately from delivery-ready
time; this type carries no configuration revision fields. Result arrays are
copied so the returned window does not retain a mutable caller list.

Seven focused scenarios pass, and the complete compiled-source Linux Domain
suite passes **825/825**, with no failures, errors, skips or unexecuted cases.
All **530** captured backend/Domain-test source hashes match after completion;
the readonly test container is removed. Build succeeds without warnings. The
first focused invocation used an unsupported runner flag, and a new artifacts
directory initially lacked restored assets; both setup failures are retained.
Correct runner arguments and normal restore/build precede the passing runs;
no assertion, deadline or retry changes are involved.

This supplies the ordering/readiness primitive required by recipient recovery.
Current Owner/Admin admission before and after IO, purpose-separated account and
membership-bound cursors, the restricted owning reader, HTTP/SignalR projection,
Activity and Demo parity, and complete current release acceptance remain
unfinished. The estimate remains **60% remaining** and no numbered acceptance
criterion or linked scenario is closed by this increment.

## Configuration recipient admission and cursors

The Application coordinator now owns an Organization transaction before every
scope, head or event read. It verifies the authenticated command actor and
requires an active owning Organization, active membership, exact tenant/actor
identity, nonempty membership identity, positive membership revision and current
Owner/Admin role. Current authority is checked again after head/event IO; an
account withdrawal, lifecycle change, demotion, membership replacement, revision
change or even an allowed-role change discards the entire page. Ordinary members
and invalid scopes cannot read the private stream head or events.

Bootstrap and invalid/foreign cursors return a current reset cursor with no
historical payload, after the same post-IO admission. Published reads use the
separate configuration readiness window. A cursor-currentness operation performs
its own owning transaction and repeats scope verification after decoding, for
the later SignalR session-IO/yield boundary.

The registered cursor codec has its own Data Protection purpose, separate from
general Organization metadata. It binds tenant, account, membership identity,
membership revision and Owner/Admin role, expires after 15 minutes, and rejects
tampering, invalid positions and unauthorized bindings. Configuration and general
Organization cursors cannot be exchanged even with otherwise matching IDs.

Fifteen focused coordinator cases and both real API-host encrypted-cursor cases
pass. The complete compiled-source Linux Domain suite passes **840/840**, with
zero failures, errors, skips or unexecuted cases. API and Domain builds have zero
warnings/errors. The complete unfiltered compiled-source Linux API suite passes
**783/783**, with zero failures, errors, timeouts, skips or unexecuted cases.
All **704** backend source hashes match the frozen verification manifest; the
owned API verification container is removed. The initial test-source syntax
error was corrected before the passing tests without changing their assertions.

At that increment only the codec was registered. There was no registered coordinator, owning event
reader or HTTP/SignalR configuration stream yet. Restricted persistence must read
actual `ready_at`; the old configuration source-event reader does not supply
publication state. Production timestamp casts were also checked: PostgreSQL
stores microseconds while immutable JSON source timestamps can retain finer
precision. The eventual reader must preserve the original JSON source clock and
actual stored publication clock without inventing either. Demo still requires
authoritative audit/publication parity before its events can be called ready.

These are recipient-security components, not a deployed recipient workflow.
Activity, current authorized transport and client recovery, all lifecycle/data/
retention/portal/UX/telemetry requirements and complete immutable-image acceptance
remain in scope. PRD-27 stays open at **60% estimated work remaining**.

## Owning configuration publication readers

The coordinator and owning event reader are now registered in both Demo and
PostgreSQL modes. Every scope, head and event operation requires the existing
Organization transaction. Production admission locks the active Organization
and current Owner/Admin membership; the coordinator also rechecks account and
membership authority after IO. Readers cannot open an independent replay scope.

Production replay reads actual `ready_at` and validates source identity against
the immutable indexed columns. PostgreSQL casts prove the stored source clock
matches the original JSON timestamp. The replay window preserves that original
JSON clock and the actual publication clock separately, while allowing only the
database's microsecond rounding. A first publication at the stored source floor
does not incorrectly reset a valid event whose JSON retains a finer timestamp.

Demo now appends its body-free configuration audit fact in the same rollback
scope as configuration state, history, receipt and first publication. It retains
the source event identity, actor, correlation and original clock. Configuration
values and events do not enter the all-member Organization metadata journal.
This audit fact does not establish completed configuration Activity delivery.

The full Linux Domain suite passes **847/847** with zero failed, skipped or
unexecuted cases. The real restricted PostgreSQL configuration/readiness contract
passes, including an actual Worker publication at the microsecond source floor,
pending-window behavior and replay denial after role withdrawal. The complete
unfiltered restricted PostgreSQL persistence executable also passes, with all
**676** recorded source/schema/contract hashes matching. Its dedicated database
instance uses the canonical CI runtime roles and is removed after completion.
Earlier clone fixtures exposed hard-coded identity observation usernames and
incompatible deletion capability role names. Identity probes now bind the actual
configured username, preserving the original sleep, expiry, rollback, recovery
assertions and deadlines. The canonical-role run passes the original complete
lifecycle and scale scenarios. The complete Linux API suite passes **785/785**,
with zero failures, errors, timeouts, skips or unexecuted cases and all **714**
recorded API/Domain source hashes matching. Both new API-host publication cases
are included; the owned verification container is removed.
HTTP/SignalR recipient transports,
client configuration recovery and the remaining full acceptance obligations
still require implementation. Estimated work remaining remains **60%**.


## Private configuration realtime and review recovery

`GET /organizations/{id}/configuration/events` exposes bounded, private,
reference-only replay after current administrator admission. Actor-bound
encrypted cursors retain original source identity and timestamp. Invalid or
foreign cursor bindings reset to current state without disclosing history;
malformed page parameters use stable errors after target admission.

The dedicated `/organizations/live/configuration` SignalR stream uses the same
owning coordinator. It rechecks current session and cursor authority before
delivery, releases cancelled subscriptions and excludes configuration values
from messages and logs. Ordinary members cannot subscribe to private events.
The existing release edge forwards this route.

The MUI page follows both membership metadata and private configuration events.
A newer revision closes stale review and reloads current authority, preserving
unsent work and any original uncertain submission. Original bytes, actor, key
and version are never silently rebased. Keyboard focus returns to the available
review/recovery action after the read. Older events for an acknowledged revision
preserve a newer review. Reconnect forces current-state checking. Withdrawal
clears protected values and offers the permitted recovery action.

The dedicated API-host transport run passed **12/12**, with no failures, skips,
timeouts or unexecuted cases and all **762** recorded source hashes matching.
Coverage includes HTTP paging/reset, guessed-target denial, origins, current
role/session withdrawal, subscription limits and cancellation. The preceding
client/page run passed **63/63** focused cases, and its complete frontend suite
passed **2,279/2,279** with **474** matching source hashes. That full run precedes
a test-only TypeScript query-option correction. The subsequent final typed-source
run passed **2,279/2,279** with all **475** recorded source hashes matching.
Final web/browser type checking, lint and the
production web build passed.

Both new native desktop/phone browser cases passed on their first attempt,
without retries or changed deadlines. They use verified sessions, strict
Production API/Worker, PostgreSQL and Nginx. They observe actual private source
frames after peer changes, recover stale keyboard review with an unsent draft,
acknowledge the subsequent human approval and clear private values after peer
session revocation. All **1,079** recorded source hashes match. Owned containers
and database fixtures were removed. This does not establish external mail
delivery or complete immutable-image acceptance.

The complete current **797-case** API suite is executing separately: the prior
**785-case** suite and the **12-case** transport run are distinct evidence scopes.
Exact-image CI for the previous main commit is queued. Required Activity
projection, suspension/deactivation, retention/schema/portal consistency, every
degraded and accessible branch, telemetry and release acceptance remain open.
Estimated work remaining: **60%**. No numbered criterion or linked scenario is
closed from this increment.

## Configuration Activity source projection

`GET /organizations/{id}/configuration/activity` provides a current administrator
with a private, newest-first page of up to 50 immutable source references. Its
purpose-separated encrypted continuation binds the Organization, viewer,
membership identity/version and administrative role. Live replay cursors cannot
be used as Activity continuations. Historical rows are read through the owning
Organization transaction and indexed configuration revision history; account,
membership and lifecycle admission is checked again after history IO.

This Activity projection uses the atomically retained configuration source.
It does not depend on separate Worker publication readiness or create another
mutable journal. Each item preserves the original event UUID, actor UUID,
version, correlation and precise source timestamp. It carries empty safe
metadata and an explicitly absent Board: the configured intake Board never
becomes the audience for private Organization configuration. Private values,
parent names and current account captions are excluded. Historical actor UUIDs
remain interpretable even when that actor cannot act anymore; event-time human
captions and governance role/capacity snapshots still require their own source
contract and are not fabricated from a current account lookup.

The full final Linux Domain suite passes **865/865**, with zero failures, errors
or skips and all **532** recorded source hashes matching. Three API-host cases
pass, including a real 53-revision owning producer and private paging/cursor
separation; all **639** applicable source hashes match. The restricted PostgreSQL
configuration/readiness executable passes with **1,810** matching source hashes,
original fine source clocks, cursor purpose separation and Activity denial
after membership withdrawal. Its existing lease, rollback, publication, Worker
private-read denial and tenant isolation checks remain in place. Owned fixtures
are removed. An initial contract compilation exposed a nullable test reference;
the correction preserves the original source assertion and the final build
has zero warnings/errors.

The earlier **797-case** API suite is still running against its frozen transport
source; it does not include these three new Activity cases. Current complete
API/image acceptance is tracked separately. The configuration Activity MUI
consumer, historical attribution/capacity work, suspension/deactivation,
retention/schema/portal consistency, full UX/telemetry and immutable-image release
acceptance remain open. Estimated work remaining stays **60%**. No numbered
acceptance criterion or linked scenario is closed by this source projection.

## Configuration Activity controls

The MUI configuration page now offers a separate reference-only Activity panel.
Administrators can load a page, continue to older changes, retry an unconfirmed
read or reload from the current head. Each item shows the original revision,
actor UUID, event UUID and precise source time. Retained configuration values
remain in the distinct history view. Empty states explain that no changes exist
in the selected page. Long references wrap on small viewports.

The client confirms the same current account and private configuration access
before and after Activity IO. Its exact response boundary rejects foreign
Organization/Board context, private metadata or unexpected fields, duplicate
UUIDs (including case variants), unsafe versions, invalid clocks and revision
gaps. Continuations retain the last displayed source boundary and cannot silently
skip or repeat revisions. Parsed references and pages are immutable.

Unconfirmed reads clear displayed Activity references and offer retry, preserving
the configuration draft and any original uncertain submission. Permission/session
denial withdraws the entire private parent context. Authority rechecking unmounts
the panel and aborts its reads; a confirmed account or revision change starts a
fresh panel rather than retaining an old continuation. Keyboard reads restore
the Activity heading while leaving focus alone when the user is editing elsewhere.
Review dialogs disable new Activity requests.

The final focused configuration/client/Activity run passes **72/72**; web and
browser type checking, lint and production build pass. All twelve shared HTTP
architecture/browser-partition verifier tests pass and the mandatory browser
inventory still includes every case in its intact file. The extended native
desktop/phone scenarios pass **2/2** on their first attempt, without retries or
changed deadlines, through verified sessions, strict Production API/Worker,
PostgreSQL and Nginx. They verify actual source references/timestamps, keyboard
focus, private-value exclusion and clearing after peer logout, while retaining
the previous stale-review/draft/realtime assertions. All **1,085** source hashes
match; owned containers and database fixtures are removed.

A fresh complete frontend suite is running against a frozen **479-file**
frontend/browser manifest. The preceding complete suite passes **2,279/2,279**
against its earlier source; it is not the new complete-suite result. The
unchanged backend has its separate **800-case** API run and current exact-image
CI remains unfinished. Event-time human captions/governance capacity snapshots,
suspension/deactivation, remaining retention/schema/portal consistency, complete
degraded/accessibility/telemetry and release acceptance still require work.
Estimated work remaining: **55%**. No numbered criterion or scenario is closed.


## Retained configuration attribution

Migration 139 captures the actor caption, Organization administrative capacity,
membership UUID and membership revision on each new immutable configuration
source. The database derives these fields from the active Owner/Admin rather
than accepting caller-supplied attribution. `ORGANIZATION_OWNER` and
`ORGANIZATION_ADMIN` describe the actual configuration permission; governance
titles do not grant that permission. Caption validation uses an explicit actor
UUID fallback when a name cannot be retained safely.

Legacy sources retain their original actor UUID and every original source field.
Their caption is `Historical actor <UUID>`, their capacity is `UNRECORDED`, and
both membership snapshot fields remain absent. The upgrade deliberately changes
the current profile name before backfill and proves it is not borrowed as
historical evidence. Tenant/actor membership identity is constrained; the stored
historical membership revision remains independent of subsequent role changes.

Attribution is private to the retained configuration journal. Existing event
JSON and metadata broadcasts remain reference-only. Private Activity responses
now carry the captured actor label and capacity through the consumer described
below. Worker readiness transitions preserve all
source fields, including attribution; even an elevated mutation cannot replace
them. Forced RLS and private capture-function privileges remain intact.

The restricted PostgreSQL configuration/readiness executable passes, including
two source-time captions around a profile rename, captured Owner membership and
capacity, and rejection of a forged attribution/readiness update. All 139
readiness entries are exercised. The original complete migration runner passes
clean/repeat/forward upgrades, concurrent serialization and failure rollback,
including its unchanged earlier upgrade checks and the new historical backfill
fixture. Both database runs used 1,817 frozen source hashes that match the staged
source. The full Domain executable passes against the completed current build.
API regression checks remain running; exact-image CI is still unfinished. No
numbered criterion or linked scenario is closed. Estimated PRD work remaining
was **55%** at the producer increment. The consumer increment below reduces the
current estimate to **52%**; lifecycle, retention/schema/portal consistency,
complete UX/telemetry and release acceptance remain required.

## Captured attribution in private Activity

`GET /organizations/{id}/configuration/activity` projects `actorLabel` and
`actingCapacity` from immutable configuration event sources. A single bounded
tenant query resolves at most 51 history identities; it does not join current
accounts or memberships to invent historical names or roles. The projection
requires an exact Organization/event/actor/revision match, valid attribution,
and a complete unique source window. Missing, malformed, duplicated or foreign
facts return `configuration_source_unavailable`, with no partial page. Current
viewer account, administrative role and membership epoch are checked again after
history and attribution IO.

Demo writes capture the account caption and actual Owner/Admin membership identity
and revision in the same rollback participation as configuration history. Later
profile changes, role reduction or historical actor deactivation do not rewrite
that capture. PostgreSQL uses migration 139's trigger-owned capture. Legacy facts
remain explicitly `UNRECORDED`; the MUI Activity panel displays that absence
instead of borrowing a current role. Private configuration values and source
membership IDs/revisions are not added to the Activity wire response.

The reviewed frozen consumer snapshot passes all 880 Domain cases, all four
focused Activity API cases, 65 client/Activity regressions, and the restricted
PostgreSQL configuration/readiness executable. The PostgreSQL check verifies
the two different captured captions around a rename through the real Activity
reader. The API check also preserves an administrator's original caption and
capacity after profile, membership and account withdrawal. Type and lint checks
pass. The extended desktop/phone native browser scenarios subsequently passed
**2/2**, with zero retries, unexpected results, flaky results or skipped cases.
They verify three captured actor/capacity entries alongside the original
keyboard review, focus restoration, stale-peer draft preservation, source-only
realtime frames and logout withdrawal assertions. The fixture used verified
sessions, Production API/Worker, PostgreSQL and Nginx. Its frozen web bundle also
included the separately tracked form/drag candidate changes: this is local
runtime evidence, not clean-main web or exact CI release-image acceptance.
Exact-image release CI remains required. No numbered acceptance criterion or
linked test scenario is closed by this increment.

The subsequent frozen Linux frontend suite passed **2,312/2,312** across **154
files**, with zero failed or pending cases and unchanged deadlines. All 480
frontend/browser source hashes match the reviewed candidate. Scalar configuration
inputs retain unchanged MUI controls across sibling edits and read/review notices;
their callbacks merge into the latest draft. A native disabled fieldset and an
independent current-disabled callback check block edits during review, including
synthetic events. The same snapshot includes the separately tested Kanban pointer
target and elapsed-time scrolling repairs. This full source-suite result does not
complete the native capacity matrix or exact-image release acceptance.

The current-main CI at `c62087a6ac59c73878ee00c87cd3437d8d55a95a` subsequently
passes **880/880 Domain tests** and **801/801 API host tests**, with zero failures
or skips, together with metadata, web quality and PostgreSQL integration. CI has
built the immutable images once and passed security; container/browser integration
and the required release gate remain unfinished in the
[same run](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38095378054).
The independent local consumer API run remains active and is not counted as a
second passing execution. The later uncommitted scrolling candidate is outside
this commit's source/image scope. This strengthens current source evidence without
completing suspension/deactivation, remaining retention/schema/portal consistency,
full UX/telemetry or PRD-wide release acceptance. Estimated work remaining stays
**52%**.

## Suspension and deactivation implementation boundaries

### Lifecycle command coordinator

#### Owning Demo lifecycle persistence

The subsequent Demo increment registers a concrete lifecycle unit/store and the
command service. Its tenant-and-actor-bound capability supplements the owning
Organization scope. Both existing account/Organization and Work mutation gates
remain held through state, original receipt, retained source and audit changes,
final session verification and commit/rollback. Generic Organization commands
cannot use this lifecycle scope, and a lifecycle transaction cannot borrow a
different tenant or actor. Every source/receipt/event identity participates in
rollback with the aggregate and authoritative Demo audit.

The store independently validates current administrative admission, exact
previous/resulting states, revision, captured caption/capacity/membership epoch,
correlation, source clock, canonical reviewed-intent fingerprint and original
receipt. Only the aggregate's status/version/update clock change; the historical
graph and membership are retained. Command source times are normalized to UTC
microseconds so future PostgreSQL persistence and Demo use the same clock shape.
This does not weaken the normal active-only operational gates.

Five pinned Linux API-host cases pass with zero failed, skipped, timed-out,
aborted or pending cases. They execute actual registration, Organization/Board/
List/Card HTTP creation and the concrete Demo service/store/transaction.
HTTP work creation/edit requests are refused while suspended without protected
content in errors; original Board/List/Card and membership records are unchanged,
and reactivation permits new work without rewriting the original Card. Additional
cases prove configuration preservation, original receipt recovery after a later
reactivation, exact audit identity/time, 256-character correlation, cross-scope
capability refusal, ordinary-member denial and complete rollback of aggregate,
audit and receipt after a write. The complete Linux Domain suite passes **909/909**,
with all **793 backend/test source hashes matching** after execution. The full
solution builds with zero warnings/errors. Owned terminal focused/Domain
containers were removed; the original full API run is separately in progress.

This is concrete **Demo application persistence**, with no new lifecycle HTTP
route or browser UI. Production PostgreSQL state migration/readers/transactions,
immutable source/audit/receipt/outbox, Worker discovery/delivery/replay, authorized
historical record access while inactive, independent portal rules, API/MUI/native
workflow and current immutable-image acceptance remain required. Existing
configuration history is verified after reactivation; it is still withheld while
suspended, so that result does not establish inactive historical access. The
retained lifecycle source is not yet published to subscribers. No FR-007/AC/test
scenario is closed; **52% estimated work remaining** is unchanged.

The preceding command-only increment introduced `OrganizationOperationalLifecycleService`
to coordinate reviewed suspension,
deactivation and reactivation against a separate lifecycle transaction/store
contract. Its admission requires current Owner/Admin capacity, active membership,
the correct tenant/account, valid captured caption and membership identity/epoch.
It never broadens `IOrganizationUnitOfWork` or its deletion-recovery exception.
The Application enum appends Suspended/Deactivated after the existing four values
so existing numeric receipt representations keep their original meaning.

The command requires the reviewed parent revision and a nonempty retry key.
It captures original actor/caption/capacity, membership identity/epoch, previous
and resulting states, incremented revision, event identity, correlation and source
time. Its store contract requires one atomic state/history/audit/receipt/outbox
write that preserves the historical graph. The coordinator rejects changed retry
intent, expired or malformed original receipts, regressed parent versions,
invalid source clocks and unsafe version overflow. Recovering an original receipt
returns that original source without restoring a later lifecycle state.
Current account and membership capacity are rechecked after IO; a denial or
cancellation requires the owning transaction to roll back all participants.

The final pinned Linux run passes **28/28 focused regressions** and **908/908 full
Domain tests**, with zero failed, timed-out, aborted, not-executed or pending cases.
All 787 captured backend/test source hashes match after execution. The cases cover
all six transitions, administrative capacity, original retry after reactivation,
cross-scope/terminal/member refusal, changed intent, stale revision, invalid input,
late account/membership/epoch/role withdrawal, source mismatch, storage conflicts,
cancelled writes and compatibility with retained numeric status receipts. Both
owned terminal test containers were removed.

At that preceding increment this was an Application coordinator and port
contract, **not an exposed or persisted suspension feature**. Concrete Demo/PostgreSQL lifecycle transactions,
status migration and readers, immutable sources/receipts/audit/outbox, restricted
runtime contracts, API registration, historical reads, independent portal
authorization, Worker discovery/delivery/replay and MUI review/retry/conflict
remain required. Existing normal operational gates remain active-only. No
FR-007/AC/scenario is closed; **52% estimated work remaining** is unchanged.

The earlier independent consumer API run has now completed: **801/801 cases
pass**, with zero failed, timed-out, aborted, not-executed or pending cases.
All 784 backend/test source hashes match the compiled frozen source and current
main. The published `fdf1106b` frontend source independently passes **2,314/2,314
across 154 files**, zero failed/pending cases, with all 480 frontend/browser
source hashes matching. Both owned terminal containers were removed. These are
full local source results; new exact-image CI and the requirements below remain
unfinished. The PRD-27 estimate remains **52% work remaining**.

Source inspection at `c62087a6` confirms ORGCFG-FR-007 is unimplemented.
`OrganizationStatus` and `PostgresOrganizationStore.ParseStatus` recognize only
Active, Archived, Deleting and Deleted. Migration 089's Organization status
constraint has the same four values. `IOrganizationService` exposes deletion,
but no suspension or deactivation command. Retaining records during deletion
does not establish a suspension workflow or authorized historical access.

The remaining implementation must change these boundaries together:

- Both Organization unit-of-work implementations currently admit an Active
  parent, with a narrowly named deletion-receipt exception for Deleting/Deleted.
  Suspension acknowledgment recovery and historical reads need explicit admission
  policies, current account/membership checks and a parent lock; broadening the
  deletion exception would grant unrelated commands access to inactive tenants.
- The existing lifecycle channel is a content-free deletion channel. Migration
  089 gives `organization_lifecycle_events` one row per tenant and permits only
  `ORGANIZATION_DELETED`. Both lifecycle readers validate that terminal event
  against the deletion request/progress and retained source clocks. Repeated
  suspension transitions cannot reuse this single-row schema or bypass its
  deletion integrity proof. Their versioned sources, receipts, immutable audit,
  acting capacity and durable Worker publication need their own consistent path.
- A lifecycle command must serialize with operational work through the existing
  parent lock and Demo gates, validate the current administrator and expected
  revision, persist state/audit/event/receipt atomically, and recover only the
  original authorized acknowledgment. A denied or stale command must leave all
  of those stores unchanged. Changing status alone is insufficient evidence.
- Historical access must remain Organization-scoped and permission-specific,
  including private configuration and independently authorized portal records.
  It must preserve original record identities, versions, attribution and source
  times without making operational writes, invitations, uploads, automation or
  stale command receipts available again. Existing active-only operational gates
  should remain enforced; historical reads require explicit routes and admission.
- The MUI workflow still needs explicit review, visible lifecycle/revision,
  keyboard and phone operation, current-authority withdrawal, uncertain retry and
  safe conflict recovery. Native and restricted PostgreSQL tests must demonstrate
  competing writes, original receipt recovery, permission loss, historical
  preservation and cross-tenant refusal before immutable-image acceptance.

This is an implementation boundary audit, not an executed suspension result.
FR-007, its related scenarios and full PRD-27 acceptance remain open; the estimate
is unchanged at **52% work remaining**.


## PostgreSQL operational lifecycle persistence

Migration 140 adds Suspended/Deactivated parent states and versioned immutable
lifecycle sources, original receipts and content-free event envelopes. Forced
tenant RLS, restricted API grants, deferred source/receipt/audit relationships
and a parent transition guard reject status-only writes at commit. Existing
records retain their prior contents during the 139-to-140 upgrade; the migration
does not invent historical transitions. API and Worker readiness require all
140 ledger entries. These events are publication intent; Worker delivery is still
required.

The dedicated PostgreSQL transaction locks the parent and acting membership,
verifies account/session admission before and after persistence, and commits only
a successful command. Normal operational admission remains Active-only. The store
records original caption, capacity, membership epoch, correlation and microsecond
UTC source time, and recovers original receipts without restoring a later state.

The pinned Linux restricted-role contract passes suspension/reactivation,
original acknowledgment recovery, ordinary-member/foreign-tenant refusal,
normal command denial while suspended, explicit failure rollback, session
withdrawal after writes, foreign history/receipt/event RLS and rejection of raw
status/pointer updates. All 140 API/Worker readiness entries pass missing/refusal/
restoration checks. Session admission in this fixture is synthetic; it does not
prove real HTTP authentication, concurrent revocation or accessible UI behavior.
The separate full migration runner passes ordered apply, repeat apply, retained
139-to-140 upgrade fixtures and migration serialization/failure/unrecorded guards.
Both runs captured 1,837 matching source files. Two earlier setup runs failed on
an incorrectly named immutability function and ambiguous SQL CASE syntax; both
were corrected before these successful runs. Disposable fixture resources were
cleaned up while the original PostgreSQL instance was preserved.

The full Linux Domain suite passes **909/909**, with 796 matching backend/test
source hashes. The full Demo API suite remains a separate running frozen source
scope. The solution builds without warnings or errors. These are local source
checks; required exact-image CI must pass for the published commit.

Lifecycle HTTP commands, explicit MUI review/retry/conflict flows, authorized
inactive historical reads, independent portal consistency, durable Worker
discovery/delivery/replay, concurrency coverage and complete release acceptance
remain required. No numbered criterion or scenario is closed. PRD-27 estimate:
**52% work remaining**.


## Operational lifecycle HTTP review and commands

`GET /organizations/{id}/operational-lifecycle` independently admits the current
Owner/Admin in Active, Suspended or Deactivated state and returns a private,
non-cacheable review: Organization/actor IDs, explicit uppercase state, revision,
source update time and acting capacity. Account and membership identity/epoch/
role are checked across IO under the dedicated retained-parent lock. This route
does not grant historical Board, configuration or portal access.

`PATCH /organizations/{id}/operational-lifecycle` requires JSON containing exactly
`version` and `status` plus a nonempty UUID `Idempotency-Key`. Status is one of
`ACTIVE`, `SUSPENDED`, `DEACTIVATED`; integer values, other states, duplicates,
missing fields, unknown fields and bodies exceeding 4,096 bytes are rejected.
Admission precedes parsing, and the command independently repeats current
authorization after the review transaction. CSRF and optional expected-actor
checks apply. Success returns original event/actor/caption/capacity, prior/new
states, incremented revision, correlation and immutable source time. Retrying
the exact original request returns its original acknowledgment even after a
later reactivation, without applying the old state again.

Responses use stable non-disclosing Problems: unauthorized target 404, withdrawn
session 401, input/retry-key validation 400, stale revision/changed or expired
retry/unchanged state 409, unconfirmed persistence/source 503. Operator metrics
use fixed review/change names and error/outcome labels; submitted values and
tenant/actor/retry identifiers never enter metric labels.

Executed pinned Linux evidence: **918/918 full Domain tests**, with 798 matching
backend/test source hashes; **8/8 focused API-host lifecycle cases**, covering
actual authenticated HTTP review, suspension/reactivation/deactivation, blocked
Board creation, stale revisions, changed-key rejection, original recovery,
malformed/oversized bodies, foreign targets, expected-actor and CSRF refusal.
The first focused run passed 7/8 and stopped its happy-path case on a header-order
assertion; the assertion now checks both actual privacy flags and the complete
rerun passes. A separate fixture-copy attempt hit Windows path-length limits
before starting any tests; its shorter uniquely named rerun passes. Restricted
PostgreSQL contracts also pass inactive administrative review and member/foreign
review refusal, capturing 1,839 matching source files. Their session admission is
synthetic; real Production HTTP/session races remain unproven. Owned terminal
fixtures were cleaned up. The current full API source suite runs separately;
the older still-running full Demo suite belongs to its preceding frozen source.

Lifecycle MUI review/confirmation, inactive historical permission routes,
independent portal consistency, Worker delivery/discovery/replay, concurrency,
remaining data/retention and accessible/telemetry/release acceptance remain
unfinished. Local tests do not prove the published immutable images; exact-head
CI is required. PRD-27 remains **52% estimated work remaining**; no numbered
criterion or scenario is closed.


## Operational lifecycle review and confirmation screen

The MUI screen at `/app/{organizationId}/operational-lifecycle` has independent
current administrative admission, allowing review/reactivation after Active-only
internal operational access ends. Organization home/settings link current
administrators to it. Uppercase links canonicalize before HTTP requests. The
review now includes the authorized Organization name; both server and client
reject a name/revision change across review IO rather than confirming a different
target. The confirmation captures the visible name, state, revision and requested
state and explains retained records and operational consequences.

The dialog starts on Cancel, retains the captured review through closing and
restores focus to its launcher, result, retry or reload control. Phone controls
retain the same review/confirmation actions without horizontal document overflow.
Account and retained-parent administrative permission are checked before and
after IO. Session/account switches or permission withdrawal remove protected
review/acknowledgment UI; session withdrawal returns to sign-in. Server enforcement
remains authoritative.

Uncertain writes retain only the original Organization/actor/revision/state/
capacity and retry key in browser session storage. No Organization name, private
configuration, caption, credential or response body is stored. Reload requires
fresh account/administrative admission, then offers an explicit retry of the same
request. Another change cannot begin while that original intent is unresolved.
Storage failure prevents submission. Malformed successful responses, 429 and
server/transport failures remain uncertain; 400/409 require a fresh review and
confirmation. A recovered older receipt is labeled as its original acknowledgment,
and current lifecycle must be loaded separately before another change. Bodies,
captions, capacity, source times and review boundaries are validated before use;
server diagnostic text is never rendered.

Executed final evidence: **23/23 focused frontend cases**, type checking, lint and
production build pass. **919/919 full Linux Domain cases**, all 798 backend/test
source hashes matching, include changed-name review refusal. Eight focused
API-host lifecycle cases and the restricted PostgreSQL lifecycle/readiness
contract pass for the name-bearing review. Separate final full frontend and older
full API source runs remain active; their results must retain their frozen source
scope. No current full-suite or immutable-image success is inferred from focused
cases.

Two native Chromium cases pass against the actual Linux Demo API and built MUI
app, at desktop 1280×900 and phone 390×844. Both authenticate the seeded
`demo@strataai.test` account using the documented Demo password, create a fresh
Organization, verify visible named confirmation and keyboard Cancel/Confirm focus,
interrupt the response after a real committed suspension, reload and recover the
original request, review/reactivate, and reject confirmation after actual logout.
The phone screenshot was inspected. All 1,125 captured backend/frontend/test
source hashes match; owned terminal API fixtures were stopped and removed.
These are Demo workflow proofs, not Production/portal/Worker acceptance.

Retained failures: the initial focused run passed 16/20, exposing result/retry
focus handoff and premature background-control queries during dialog exit. The
focus/confirmation implementation and transition-aware checks were repaired;
the expanded final run passes 23/23. An earlier full Domain run omitted the
newly added name-change case because its binary predated that edit; the test
project was rebuilt without incremental reuse before the final complete run.
An initial native setup used a network mode
that could not publish its local port; its disposable fixture was removed, the
port setup corrected and both corrected runs passed. Earlier scope results remain
historical evidence rather than current-source claims.

Required remaining work includes permission-specific inactive historical reads,
independent portal consistency, durable lifecycle Worker delivery/discovery/replay
and authorized realtime recovery, full Production concurrent-write/session-loss
acceptance, remaining data/retention, UX/telemetry and exact-image release evidence.
PRD-27 estimate is now **50% work remaining**. No numbered AC or linked scenario
is closed by this partial workflow.

## Inactive Organization lifecycle directory

`GET /organizations/operational-lifecycle-directory` provides an actor-bound,
private/no-store directory for current Owners and Administrators of Active,
Suspended and Deactivated Organizations. Existing membership routing supplies
at most 51 ordered hints; the service reviews at most 50 and rechecks admitted
Organizations after page IO. Routing does not grant administration. Ordinary
members, withdrawn memberships and terminal parents contribute no private names.
An empty authorized page can still have a continuation; malformed/regressed
hints or lost sessions return no partial response. The directory performs no
mutation or receipt lookup and does not reopen normal operational access.

The standalone MUI `/app/lifecycle` page is reachable from the Organization home
and lifecycle review. It checks the account before and after loading, validates
the exact actor-bound response, and clears previous names during reload, paging
and foreground refresh. Entries show name, lifecycle and revision and lead to
the existing independently authorized review/confirmation workflow. Pagination
is bounded; first-page recovery remains available after a failed continuation.

Current evidence: **924/924 full Linux Domain tests**, **10/10 focused API-host
tests**, **6/6 directory UI tests**, typecheck, lint and production frontend build
pass. Restricted PostgreSQL contracts pass with 1,848 matching captured source
hashes, including suspended Owner rediscovery and ordinary-member exclusion.
Two native Chromium desktop/phone cases pass against the actual seeded Demo API
and built MUI app, with 1,130 matching backend/frontend/test hashes. Both recover
a committed suspension, rediscover its named entry through this directory, open
canonical review and reactivate. The original PostgreSQL fixture is preserved;
owned terminal fixtures were removed.

Retained full frontend results on earlier source were 2,334/2,336 and
2,335/2,337. The failures were shell heading discovery, Card-detail modal focus,
List-copy recovery and configuration-review timeouts. The four selected cases
pass subsequent focused reruns with unchanged test timeouts. This is not a full
suite pass. The current complete frontend run uses one worker to reduce resource
contention and remains a separate pending result. An older frozen Demo API source
passes 806/806; eleven later backend source differences prevent treating that as
current-source evidence. The newer full HTTP API run and immutable-image CI
remain pending.

Inactive historical reads, independent portal consistency, durable lifecycle
Worker delivery/replay and authorized realtime recovery, Production concurrency,
remaining retention/data and release acceptance are still required.
**50% estimated PRD-27 work remaining**; no numbered criterion or scenario closes
from this directory increment.

## Operational lifecycle delivery capability

The Application delivery contract now distinguishes operational lifecycle events
from the existing deletion-completion and configuration channels:
`ORGANIZATION_OPERATIONAL_LIFECYCLE_EVENT_READY`, with service identity
`organization-operational-lifecycle-delivery`. Its handler accepts only a complete
owning job/actor/Organization/worker/lease scope and one body-free event UUID.
Deletion, configuration and mixed service capabilities, malformed/private
references, unavailable sources and cancellation cannot return successful
dispatch. The storage contract requires binding the exact live lease to the
immutable transition; retained attribution does not grant current subscriber
access. A bounded scope-reader port prepares the separate Worker discovery loop.

The rebuilt full Linux Domain suite passes **936/936**, with 803 captured
backend/test source hashes matching. This adds twelve dispatcher cases. This
contract is not registered as a running producer/Worker pipeline yet: schema 140
still records publication intent only. Production lease-guarded publication,
discovery, runtime grants/readiness, Demo delivery, independently authorized
replay/realtime and their integration/concurrency tests remain required before
claiming event delivery. **50% estimated PRD-27 work remaining**; no acceptance
criterion or linked scenario is closed.

## Production operational lifecycle publication and delivery

Migration 141 binds the body-free operational lifecycle envelope to its immutable
history and AuditEvent and publishes a typed durable job in the same transaction.
Existing envelopes retain their original JSON/history/receipt/source clocks and
receive exactly one pending publication. Forced tenant RLS remains enabled.
`ready_at` is a one-time delivery clock; `updated_at` is generated from readiness
or the original source clock. API accounts cannot borrow delivery privileges.

Restricted Worker delivery checks the exact Organization/job/actor/event,
correlation, idempotency reference, worker and unexpired lease. It verifies the
immutable history, preserves the first readiness timestamp on redelivery and
rechecks the lease before commit. A committed fact remains deliverable after
later parent revisions or original actor deactivation; subscriber access still
requires an independent current authorization check. Completed, expired and
superseded leases cannot publish.

The separate Worker now registers the operational lifecycle handler and a bounded
discovery loop. `STRATAAI_ORGANIZATION_OPERATIONAL_LIFECYCLE_DISCOVERY_ENABLED`
defaults to enabled in Production; explicitly enabling it outside Production is
rejected. Discovery returns at most 100 tenant identifiers, seeks and wraps, and
finds pending or expired claims without requiring an active parent. Each pass
uses forced tenant RLS and a dispatcher that claims only the operational job
type/service pair, with count/time bounds and existing retry/backoff behavior.
Runtime provisioning grants only the Worker the delivery/discovery/claim functions;
API and Worker readiness require migration 141.

Evidence: solution build passes without warnings/errors; the full Linux Domain
suite passes **936/936**. Final restricted PostgreSQL contracts pass with **1,856
matching source hashes**. They cover wrong lease/actor/tenant/event refusal,
expired-lease rediscovery/reclaim and rejection of the previous lease, publication
rollback, actor deactivation, original-clock redelivery, completion withdrawal,
unrelated-provider job isolation and API function denial. All 141 migration ledger
entries are individually refused/restored by both runtime accounts. These tests
invoke the real restricted adapters and handler; they do not prove a deployed
Worker process, Production HTTP sessions or recipient realtime delivery.

The complete populated upgrade runner passes, including exact old envelope,
history and receipt preservation, original clocks, one queued job per event,
repeated execution, serialized migration runners and rollback/unrecorded rejection.
Its frozen source predates only the final expanded C# persistence contract;
migration/upgrade scripts are unchanged. An initial long-path setup failed before
database creation; the shorter-path run passed. Owned terminal databases, roles,
containers and credential files were removed; the original PostgreSQL was kept.

The complete frontend run has now finished **2,343/2,343**, zero failed/pending,
with 329 matching frontend source hashes and unchanged test timeouts. Earlier
failed source runs remain recorded above. The older full HTTP API run and
exact-image CI are still separate pending evidence.

Demo delivery, independently authorized operational lifecycle replay/realtime,
actual hosted Worker recovery/concurrency, inactive historical and Portal access,
remaining data/retention and release acceptance remain required.
**49% estimated PRD-27 work remaining**; no numbered criterion/scenario is closed.

## Authorized operational lifecycle replay and Demo publication

`GET /organizations/{id}/operational-lifecycle/events?afterVersion=0` is a
private/no-store, current-session/current-internal-membership replay channel for
Active, Suspended and Deactivated Organizations. It does not grant lifecycle
administration, reopen operational commands or confer Portal authorization.
The owning retained-parent transaction checks current account/membership and
rechecks membership identity, revision/capacity and session after event IO.
Changed parent state returns a conflict; lost authority returns no partial page.
An optional expected-actor header rejects switched accounts.

The response binds Organization, recipient actor, current lifecycle and parent
revision, requested `afterVersion`, optional `nextVersion`, `pending` and events.
Events contain only event/Organization/actor IDs, event/entity type and ID,
revision, previous/current lifecycle, source acting capacity and source time.
Private names, saved actor captions, request correlation and retry keys are
excluded. Current internal Members may read these facts; administrative review
and changes still require current Owner/Admin authority.

Reads inspect at most 51 ordered sources and return at most 50 facts. They include
unpublished rows internally and return only the ready prefix, preventing a later
ready event from hiding an earlier publication gap. `pending` signals the first
unpublished source encountered. Follow `nextVersion` to load another page; for
subsequent polling, retain the last received event revision. When no new event
is returned, retain the existing position and retry. A missing next-page cursor
does not indicate that future events are impossible. Negative, repeated,
nondecimal or beyond-head positions are rejected. Malformed, foreign, duplicate
or unbounded sources fail closed. Reads perform no mutation or receipt lookup.

PostgreSQL reads use the owning lifecycle transaction and forced tenant RLS,
binding the envelope ID/revision/time to immutable history and reading Worker
readiness. Demo now simulates immediate process-local publication under the
same Organization/Work rollback gates, following its configuration-event
simulation pattern. Publication timestamps roll back with history/receipts/audit
when a command fails or loses its session. Demo is not a durable leased Worker
pipeline and its data still disappears on process restart.

Evidence: solution builds without warnings/errors; full Linux Domain **943/943**
and focused API **12/12** pass, each with 810 matching captured backend/test source
hashes at completion. Domain cases cover publication gaps, bounded continuation,
membership/session/parent changes and malformed source refusal. API cases prove
ordinary-member content-free replay while administrative review is denied,
cross-tenant/actor/cursor refusal, privacy headers and 51 real Demo transitions
with 50/1/empty continuation. Restricted PostgreSQL contracts pass with 1,860
matching source hashes, including pending suspension, foreign refusal, partial
published-prefix recovery while the original actor is deactivated, both delivered
facts and continuation after the first. A fixed, identifier-free replay telemetry
label was added afterwards and the solution rebuilt successfully. The unchanged
frontend retains its preceding 2,343-case result. The older full HTTP API run and
current exact-image CI remain separate pending evidence.

Required remaining work includes realtime subscription/consumer integration and
reconnect behavior, hosted Worker and Production HTTP race acceptance, inactive
historical/independent Portal access, remaining data/retention and release evidence.
**48% estimated PRD-27 work remaining**; no numbered criterion/scenario is closed.

## Operational lifecycle browser replay validation

Restricted PostgreSQL concurrency now verifies the shared owning-parent lock between lifecycle changes and operational board creation. Four cases cover both contender orders for suspension/deactivation. The fixture observes actual lock waits before releasing its gate; it does not infer order from a fixed sleep. Admitted work first commits and remains retained. Inactivity first rejects later work atomically, leaving no board, Work event/job or Board audit rows. The lifecycle source, history and original receipt remain exactly once. The focused persistence executable passes with all 1871 captured source files matching at completion. Actor/command context is synthetic; this is distinct from HTTP session-race acceptance. The contract is included in both focused lifecycle and full persistence execution.

Actual Production API/Worker hosts now pass isolated automatic lifecycle publication and restart recovery with PostgreSQL forced RLS and distinct restricted roles. HTTP-created suspension/deactivation facts remain pending with the Worker stopped; the owning inactive review does not restore internal operational admission. Both inactive Organizations are discovered after their source actor is deactivated. A fresh second Worker reclaims controlled expired leases and preserves original ready clocks, immutable sources and unrelated jobs. This uses no explicit Organization scope IDs. The successful fixture captures 1868 source files with zero mismatches at completion. These local hosts use the fresh compiled API/Worker on a pinned runtime; exact release-image proof remains pending.

The command CI job now invokes `scripts/ci/test-organization-operational-lifecycle-delivery.sh` against the release images with `compose.operational-lifecycle-test.yml`. It isolates typed discovery, restores the normal Worker on exit, keeps cookies/responses private, and checks the same inactive-parent/source-actor/reclaim invariants. Its disposable account follows the established production notification-test admission fixture; email provider verification is a separate requirement. Shell syntax and YAML validate locally. The initial local host setup failures were Secure cookies withheld by the loopback HTTP client; explicit fixture-cookie forwarding fixed them without changing production cookie policy.

Release browser coverage now includes `tests/browser/organization-operational-lifecycle-live.spec.ts`: two distinct accounts at desktop and phone widths, real invitation acceptance, MUI keyboard confirmation, current-authority remote review, operational withdrawal, real WebSocket interruption and missed-source replay, readmission and logout. It is explicitly included in the exact-image Demo foundation step and automatically collected by the four existing complete Production shards. The established Production account fixture admits only disposable test accounts; provider email delivery remains separately tested. Local execution of these repository cases passes 2/2 in Demo with 1304 captured source files and zero mismatches. Production release execution remains pending. Browser typecheck and actual shard collection pass (360 cases, 137 files, four intact partitions).

The earlier frozen web regression completed 2364/2364 on pinned Linux Node, preserving default case timeouts. Four later captured files differ, so the result applies to its frozen review-subscriber scope, not the subsequent shell increment. Current release gates and hosted Production Worker behavior still need verification.

Native live collaboration has now passed on desktop (1280×900) and phone (390×844) against a fresh no-incremental API build and freshly built web assets. Each run uses the known seeded Demo owner plus a distinct newly registered and email-verified account, accepted through a real INTERNAL administrator invitation. The fixture proxies WebSocket upgrades and configures the actual public origin; it observes three operational stream pages per recipient. A reviewed dialog closes on the owner's actual MUI suspension, the active home withdraws, and a real socket interruption followed by reactivation recovers the missed source from cursor 2. The home restores after current admission; actual logout withdraws the named review. Both runs pass, with 1146 captured source files and zero mismatches. The phone screenshot was inspected and the viewport has no horizontal overflow.

Two retained fixture failures preceded this result: requesting verification under Demo's default already-verified policy, then attempting live admission without configuring the required public origin. The successful fixture explicitly enables verified-email policy and origin protection; no product guard was bypassed. These are Demo process-local publication results, not Production Worker persistence/restart proof. The older frozen pre-stream API run also completed 813/813, but three captured backend/test files subsequently changed and new stream files were not in its capture; it does not prove the current full suite.

The internal surface admission boundary also subscribes after confirming the current account. An admitted suspended/deactivated parent snapshot withdraws operational children immediately. An active snapshot must pass a fresh INTERNAL surface-access read before the subscriber's final account check and before content is restored. The boundary retains the existing bounded polling/focus/manual recovery. Live decisions increment an admission generation so an older in-flight polling response cannot restore withdrawn content. Transient interruption hides retained child recovery state; fresh denial unmounts protected content. The independent denied-content route continues to require its own current authorization. The Portal never subscribes to this internal-member stream and retains independent surface checks.

Surface admission and full app tests pass 20/20, including immediate suspension/deactivation, fresh reactivation admission, Portal separation, older polling grant fencing and transient/fresh-denial withdrawal. Typecheck and targeted lint pass. This remains component evidence; native multi-user collaboration and hosted Production delivery are still required.

The lifecycle review page now consumes the operational SignalR stream. Before exposing an update, the subscriber checks the current account, performs a fresh administrator review on the consumer surface with the expected-actor header, and checks the account again. The review snapshot must be at least as recent as the stream parent snapshot and agree on status at equal revisions. Private names come only from the newly admitted review, never from source facts. Remote changes close stale confirmation dialogs and produce a non-disruptive status announcement. A disconnect disables review until a fresh manual load or admitted live update succeeds. An unconfirmed original request retains its independent recovery flow.

The subscriber serializes pages, advances only to a delivered source revision, and resumes from that cursor after reconnect. Per-subscription cancellation and generation checks fence late account/admission results and old observers. A 64-page pending backlog bound stops the connection and preserves its cursor for retry. HTTP 401, account replacement and consumer permission withdrawal have separate recovery consequences. Ordinary failures use fixed user-facing messages and bounded retry delays. Lifecycle review remains outside the active operational shell.

Focused browser tests cover replay parsing, subscriber reconnect/gaps/account replacement/admission withdrawal/generation replacement/backlog/cleanup, and MUI fresh review, stale dialog cancellation, accessible announcements and interruption recovery. These are component and simulated transport tests; they do not prove native two-user WebSocket behavior, the active shell consumer, or hosted production Worker delivery. Those remain release requirements.

The browser replay parser binds the exact HTTP page or SignalR envelope to the expected Organization, current recipient and requested cursor. It rejects extra fields, foreign entity identifiers, duplicate event identities, unordered or unsafe revisions, invalid transition/event-type pairs, and clocks outside UTC microsecond precision. Pages are bounded to 50 facts; a continuation must equal the last delivered source position. A pending publication can leave the cursor unchanged. Intervening configuration revisions need not have lifecycle events.

The validated parent snapshot remains distinct from historical source status. Neither supplies operational permission: consumers must refresh current account and surface admission before exposing private state or enabling work. Nine focused browser tests cover these protocol boundaries, including 50/1 recovery and publication gaps. This parser is groundwork for the subscriber; the MUI subscription and native multi-user verification remain outstanding.

## Operational lifecycle SignalR stream

`/organizations/live/operational-lifecycle` exposes `Watch(organizationId,
afterVersion)` for current internal members. It uses the independently authorized
replay service for Active, Suspended and Deactivated parents, retaining normal
operational and Portal authorization boundaries. Each iteration authenticates
the original session, reads a bounded page, authenticates again, then discards
that earlier page and re-enters owning replay admission before delivery. This
prevents session IO from carrying a captured page past membership withdrawal.

Frames bind Organization and recipient user to the shared HTTP/SignalR wire page.
Both transports use the same content-free projection; private names, saved actor
captions, request correlation and retry keys are absent. The stream publishes an
initial snapshot, changed current lifecycle/revision/pending state, published
facts and periodic heartbeats. It advances only through returned ready events,
drains bounded ready pages, waits at publication gaps and resumes from a supplied
last event revision after reconnect. Session or membership denial closes the
connection. One active subscription is allowed per connection; cancellation
releases its ownership. Existing origin enforcement and transport buffer limits
apply to the new route.

The solution builds without warnings/errors. **21/21 focused lifecycle API cases**
pass, including nine new real WebSocket scenarios: ordinary-member suspension
delivery, membership removal/logout, missed reactivation on reconnect, 50/1
stream paging, missing/foreign origin, foreign membership, subscription limits
and cancellation, and controlled membership withdrawal during the session IO
between page reads. The latter decorates actual session authentication and
withdraws the real Demo membership after a committed source was captured; no
captured stream item is received. **9/9 existing live-transport cases** also pass.
Both runs capture 813 matching backend/test source hashes.

Retained failures: the initial focused run passed 19/20, and the diagnostic run
with the additional session-IO probe passed 20/21. Stack inspection located the
failure in the shared test receiver's single-fragment assumption, while reading
the 50-event page. The receiver now reassembles valid WebSocket fragments within
its existing 131,072-byte bound before parsing a SignalR record. No test timeout
or message-size limit was increased. The final 21-case and nine-case runs pass.
Owned terminal fixtures were removed. Domain and PostgreSQL logic is unchanged
from its preceding scoped evidence. The separate full API run predates these
stream/receiver changes and remains active; exact-image CI is separate pending
release evidence.

MUI subscriber integration and account-switch/reconnect UI recovery, native
multi-user/phone behavior, actual hosted Worker/Production races, historical and
independent Portal access, remaining data/retention and full release acceptance
are required next. **47% estimated PRD-27 work remaining**; no numbered criterion
or linked scenario is closed by the server stream.
