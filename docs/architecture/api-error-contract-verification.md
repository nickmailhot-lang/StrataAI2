# API error envelope verification

## Live diagnostic failure isolation — 2026-10-15

The live-hub audit finds **24 unguarded warning writes in ten existing hubs**.
They run after a session/read/admission failure but before the safe hub error
and connection abort. A throwing diagnostic provider can replace that intended
error. The affected paths now use the existing `ApiDiagnostics.Warning` guard;
warning templates, levels, correlation fields, denial codes, authorization and
cancellation filters are unchanged. This covers identity, notification, private
stars, Organization Board/metadata/configuration/lifecycle, invitations and
existing retention-hold streams without expanding those features.

The [direct iterator regressions](../../tests/StrataAI.Api.Tests/LiveDiagnosticFailureTests.cs)
execute the real async iterators with controlled session/storage faults and
logging providers. Before repair, **11 of 32 cases fail** because a logging
exception replaces the intended `HubException`. After repair, **32/32 pass**:
all ten session-failure paths with both healthy and throwing writers, ten caller
cancellation paths, and identity replay-read failure with both writer modes.
Failures abort the connection, release subscription state and preserve the safe
code without yielding a page. Cancellation remains cancellation. Healthy
providers still receive one warning with only the original template and bound
correlation field, excluding cookie, provider exception and diagnostic details.

Two additional [HTTP/SignalR transport cases](../../tests/StrataAI.Api.Tests/LiveDiagnosticFailureHttpTests.cs)
pass through the actual API test host: an already authenticated WebSocket sees
a session-service or identity-read fault while its hub logger throws. The
transport closes within the unchanged eight-second deadline, emits no profile
stream item and discloses neither failure detail. A completion error alone is
not accepted as proof of connection abort. These are injected service faults,
not an observed disclosure exploit against an older deployed release.

All **64 selected existing live-update transport/admission cases** also pass,
excluding the new direct class and transport method from that selection:
**98 focused cases total**, with zero skipped or failed. The Release solution
and final API test build have zero warnings/errors. Existing successful streams,
origin/cursor rules, access withdrawal and recovery remain covered by those
unchanged regression assertions; individual test deadlines are retained.

The direct run preserves **794 source and 415 binary files**; the transport run
and existing-live run preserve **795 source and 415 binary files**, with zero mismatches. Owned test
containers are removed and original Docker inventory restored. No database,
existing local service, new image build/pull/import, external provider or load
test is used. The first transport invocation refuses the Windows-generated
content-root path before hosting; the existing explicit Linux test-root setting
corrects only that private launcher. Source inputs and binaries remain unchanged.

The transport host uses controlled Demo persistence in a retained Linux runtime
with current compiled assemblies, not current immutable release images or
managed diagnostics/providers. Full current CI, deployed failure/authorization
acceptance and the remaining PRD/architecture scope remain required. PRD-02 stays
open at **16% estimated work remaining**, a planning estimate. Large load testing
remains deferred until after the operational MVP.

PRD-21 API-FR-003 requires code, message and correlationId in error responses.
The shared Problem Details customization preserves existing domain codes and
public titles and adds the actual response correlation reference. Empty errors
from authentication, CSRF or malformed JSON binding use the same contract.
HEAD responses retain bodyless HTTP semantics. A fallback Problem Details writer
also applies the contract when the default writer cannot negotiate a JSON Accept
header. Runtime serialization preserves validation errors.

The 11 HTTP host regressions cover authentication, CSRF, missing resources,
malformed binding, JSON/HTML/plain-text Accept headers, sanitized request
references, domain validation with identical canonical Board state, and HEAD.
All 11 pass. Before customization, all four initial envelope cases failed; the
non-JSON Accept regressions then exposed two additional failures before the
fallback writer repair. Existing domain assertions were retained.

After a successful solution build, the final Linux run passes all **993 Domain
tests and 923 API tests**, with zero failures or skips. The API run completes in
3,034,451 ms. Its 2,530 captured source/runtime file hashes have zero mismatches,
confirmed independently after completion. All owned containers are removed;
the pre-existing stopped PostgreSQL container and volume are preserved.
The earlier Linux baseline passed 993 Domain and 920 API cases before the
fallback writer and three media-type regressions were added.

These are compiled-source tests hosted in the pinned Linux ASP.NET runtime,
with the application's real entry point and middleware and controlled Demo
persistence. The explicit ASP.NET testing content root points at the mounted
source. This is not a build-once retained-image, PostgreSQL/RLS, managed-provider,
or complete PRD acceptance claim. The focused client compatibility tests also
pass: 20 Work request and 22 shared Problem/fetch cases.

At the envelope checkpoint, unexpected exception and failed-readiness response
contracts remained incomplete. Private probes reproduced both gaps; an isolated
exception-handler draft proved
safe correlated 500 responses, subsequent host recovery, private-content-free
logs, preservation of cancellation/already-started responses, and bodyless HEAD.
Those draft results do not certify a production implementation. Optional field
error mapping, full endpoint/consumer inventories and current immutable CI
remain required by the [PRD-21 acceptance map](prd-21-acceptance.md).

The [operational MVP milestone](operational-mvp-milestone.md) prioritizes these
core reliability and deployment gaps while retaining the complete roadmap.

## Unexpected failures and readiness

The API now handles unexpected exceptions inside the correlation scope and
outside the downstream request middleware. Before a response starts, it clears
the unstarted output and returns a fixed `internal_error` 500 with the actual
correlation ID and `Cache-Control: no-store`. It records a fixed error event and
correlation ID without an exception object, message, stack, URL, query or body.
HEAD remains bodyless. Started responses and cancelled requests preserve their
original exception and response instead of appending an error document.
Existing specialized database role/schema middleware keeps its 503 codes.

Failed `/readyz` responses retain `status: not-ready` and add `runtime_not_ready`,
a fixed public message and the actual correlation ID. Successful readiness and
build identity retain their existing behavior.

All four new HTTP baseline cases failed before this implementation. The final
focused run passes **20/20** envelope, unknown-error, logging, recovery, readiness,
specialized 503 and boundary cases with zero skips; the successful readiness
regression passes **1/1**, and all **7/7** runtime composition/startup-refusal
cases pass. The final solution build has no warnings or errors. These results
include the eleven preceding envelope regressions. The earlier full 993/923
Linux run certifies the envelope checkpoint, not this later exception handler.
Complete current source and immutable-image CI remain required.

See [the middleware](../../src/StrataAI.Api/UnexpectedApiErrorMiddleware.cs) and
[HTTP/boundary regressions](../../tests/StrataAI.Api.Tests/UnexpectedApiErrorTests.cs).

## API-owned middleware diagnostics isolation — 2026-10-15

Correlation scope creation/disposal and the unexpected-error, runtime database
security and CSRF diagnostic calls now use an API-local exception guard. A
throwing log provider must not prevent the downstream request, replace its
original failure, or prevent an already chosen 500/503/403 response. Structured
templates, levels, event identity and correlation fields remain unchanged.
Provider failure is not logged recursively; raw request/provider exceptions
remain excluded from these diagnostic messages.

The new middleware regression class runs **14** cases. Before the guard,
**10 fail and four pass** in 0.424 seconds. Afterward, all **14 pass** in
0.388 seconds: scope creation/disposal faults, successful and throwing downstream
delegates, private GET/bodyless HEAD errors, started/cancelled boundaries,
role/schema 503s, missing-header/cross-site CSRF refusal, one admitted mutation
and healthy structured logging. Exact downstream call counts and private error
exclusion are asserted.

Five additional actual HTTP-host tests pass in **11.204 seconds** with a
provider throwing from the affected warning/error categories. They retain
correlated sanitized 500s for JSON/HTML Accept headers, specialized role/schema
503s, a 403 CSRF code and a subsequent healthy request without another diagnostic
call. An initial new HEAD HTTP probe received 405 because `/api/health` maps GET;
the final HTTP cases exercise mapped requests. Direct new and existing middleware
tests retain the HEAD contract; no production route or existing test is changed.
The existing **three** started/cancelled/HEAD boundary cases also pass in
0.281 seconds: **22 selected cases** across three invocations, not the full suite.

The full solution builds with zero warnings/errors. The tests run current
compiled assemblies in the retained Linux .NET runtime with network disabled
and read-only assembly/source mounts. The HTTP host uses its controlled Demo
composition, not PostgreSQL or managed providers. Each final invocation verifies
all **922 captured source inputs** and compiled binaries unchanged and restores
the original Docker inventory after removing its container. No image build,
download/import, original database use or large load occurs.

This protects the four named application-owned middleware boundaries. It does
not bound a provider that blocks indefinitely or protect calls made internally
by the host/framework or other diagnostic consumers. Full current immutable CI,
deployed logging and the remaining PRD/architecture acceptance remain required.
See [the guard](../../src/StrataAI.Api/ApiDiagnostics.cs) and
[regressions](../../tests/StrataAI.Api.Tests/ApiMiddlewareLoggingTests.cs).

## Rate-limit diagnostic creation and writing failures — 2026-10-15

The rate-limit rejection callback now guards both category logger creation and
its warning write. Previously either optional provider failure escaped into
the unexpected-error middleware and replaced the intended 429 with a 500.
The API-local guard preserves `rate_limit_exceeded`, the safe public message,
correlation reference and `Retry-After`. The category, warning level, structured
template and correlation field remain unchanged; provider failures are not
logged recursively. Limit configuration, partition keys, windows, lease handling
and response generation are unchanged.

The new regression class runs **12** cases. The unchanged baseline fails **eight**
and passes four in **10.115 seconds**; both throwing-provider HTTP cases reproduce
500 instead of 429. After repair all **12 pass in 9.968 seconds**. Nine callback
cases cover healthy/throwing logger creation/writing, absent retry metadata,
fractional rounding and the one-second floor. Three actual HTTP-host cases use
the supported minimum configured auth quota of ten: ten sequential failed logins
are admitted, the next two are both rejected with the original safe 429 contract,
and an unrelated health request still succeeds. Sink/category call counts and
structured private-content exclusion are asserted. This is a small functional
fixture, not throughput, capacity or load testing; production defaults are unchanged.

The existing five middleware HTTP cases also pass in **10.649 seconds**, retaining
unexpected-error, role/schema and CSRF responses with throwing providers:
**17 selected cases** across two final invocations, not the full source suite.
The full solution builds with zero warnings/errors. Each invocation matches all
**923 captured source inputs** and compiled binary hashes, and restores original
Docker inventory after removing its test container. Current compiled assemblies
run with read-only source/test mounts and network disabled in the retained Linux
.NET runtime; the actual HTTP host uses controlled Demo persistence. No original
database, managed provider, image build/download/import or large load is used.

As with the preceding guard, blocking providers and framework-internal calls
remain outside this exception boundary. Complete current immutable CI and
deployed logging remain required. See [the callback](../../src/StrataAI.Api/Auth/SecurityRateLimits.cs)
and [callback/HTTP regressions](../../tests/StrataAI.Api.Tests/RateLimitLoggingTests.cs).

## Full hosted source verification for the reliability checkpoint

[CI run 38287805799](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38287805799)
for exact commit `1d03b0c9` passes the complete hosted backend source suite:
993 Domain and 932 API cases, zero failures or skips. Web quality, PostgreSQL
integration, the source-quality gate and build-once image job also pass. This
extends the handler/readiness focused proof to the full source suite. Security
and container integration were still in progress when recorded. Newer main
commits add browser/component checks and documentation; this result certifies
the named checkpoint and does not establish a completed current release.
