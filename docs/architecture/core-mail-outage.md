# Core task work during a mail-provider outage

This is partial [ARCH-08-AC-003](https://github.com/nickmailhot-lang/StrataAI2/issues/89)
evidence for operational task management. Mail failure must leave existing
verified users able to work with their protected Boards, Lists and Cards.
Pending identity mail remains durable and retries through the separate Worker.
The wider AI/meeting, managed-provider and complete release acceptance
criteria remain in force.

## Mandatory controlled scenario

The normal exact-image identity-mail stage sources
[the core outage contract](../../scripts/ci/core-mail-outage.inc.sh) after its
owner has actually received and consumed the Worker-delivered verification
proof. It runs before the profile is restored for subsequent browser tests.
This phase does not edit delivery availability times, leases, expiry, attempt limits,
backoff, runtime roles or verification state to make the scenario pass.

1. Admit the expected Production API build and this run's provider instance.
   Require the verified active owner, active Organization ownership and a private
   Board. Non-CI, unbound revision, Demo and foreign-provider targets fail before
   any control or database access as applicable.
2. Enable persistent HTTP 503 failure in the isolated provider. Submit the same
   password-recovery key/body twice and require identical neutral 202 replies
   with exactly one new durable reset-delivery job.
3. Observe the actual Worker attempt and durable retryable `PENDING` state with
   the fixed unavailable code. The provider must record the attempt without a
   delivery effect for that key.
4. While failure remains active, require API readiness; create one List and
   Card; edit the Card from revision 1 to 2; and reload its authoritative Board
   snapshot. Match the persisted title/version/Organization and exactly three
   relevant audit additions. Anonymous Board access stays 404 without the Card
   title/ID, and the original authenticated session remains usable.
5. Restore the provider and let normal backoff run. Its existing lost-reply
   behavior first accepts one effect with 503, then returns the same receipt on
   retry. Require exactly one provider entry, at least two effect-path attempts,
   the same stored receipt, terminal `SENT`, cleared lease/worker/error fields,
   readiness and unchanged Card content/revision.

The API and Worker container IDs/start times must remain identical throughout.
There is no restart, dependency bypass or extra image build in this scenario.
Failure cleanup resets only the provider already admitted as this fixture's
instance. All message bodies, cookies, reset proofs and private identifiers stay
in the fixture; the job log prints only fixed phase/result messages.

## Executed native evidence — 2026-10-15

The complete setup/scenario/cleanup passes in **159.262 seconds** against fresh
PostgreSQL **17.11** with all **149 migrations**, canonical restricted API,
Worker and identity-mail roles, strict verified-email policy and separate actual
API/Worker processes. Owner registration, mail verification, login and initial
Organization/Board creation all use the real HTTP interfaces. The provider
effect and verification proof originate from actual Worker HTTP requests.

All **717 captured source inputs and 71 API/Worker binary files** match after
execution. Cached `24f56246` API/Worker images supply Linux runtimes with current
local outputs mounted read-only. Their compilation metadata is
`7ac8b5dc92c6a70b51d647d6c976629726475372`, version `0.1.0-1736`; the compiled
mail repair and current source are exercised, but this does not certify a
current immutable application release. The API/Worker/PostgreSQL containers,
owned network and temporary provider thread/socket are retired, and the full
original Docker inventory is restored. No image is built/pulled/imported, the
original database and local Demo are unused, and no external email is sent.

The first attempt stops before enabling an outage because the fixture expects
lowercase Board visibility. The API uses `PRIVATE`; the final check matches that
wire contract while retaining the private-Board requirement. Both attempts
restore inventory and have zero frozen-input differences.

All **five provider-fixture cases**, **seven admission/caller cases** and **272 CI
guard tests** pass. They preserve legacy one-shot refusal/lost-ack behavior,
refuse invalid controls, ensure an outage has no effect, guard target admission,
and reject omitted source checks, an unbound provider instance, bypassed mail
execution or ignored failure. Shell syntax and workflow lint pass. The new
fixture controls are source-only CI tools, not release services or production
provider controls. The normal browser-notification group must execute this
contract against the three retained application images before release.

## Desktop and phone browser evidence — 2026-10-15

The [dedicated browser scenario](../../tests/browser/core-mail-outage.case.ts)
passes both complete cases at **1280 and 390 pixels**, with **zero skips,
retries or flaky outcomes**, in **307.267 seconds**. Each case registers a real
account, waits for Worker-delivered verification, consumes its proof and signs
in through the browser. Pending verification correctly refuses login. There is
no SQL verification bypass or mocked application response.

With sustained provider HTTP 503 failure and an observed reset-delivery attempt,
keyboard actions create one List, create one Card and save one Card title edit.
The test observes exactly one request for each write, checks their successful
responses and reloads the edited Card. The authoritative Board snapshot retains
exactly one matching Card at revision 2. Anonymous Board access remains 404,
readiness and the original session remain usable, and all **six accessibility
and viewport-overflow checks** pass across the two cases. On provider recovery,
normal retry and lost-ack behavior produce one effect for the same delivery key;
the edited Card survives another reload. This browser observation proves the
provider acknowledgment; the separate HTTP/SQL contract above proves terminal
`SENT`, receipt persistence and audit additions.

The full local setup, execution and cleanup takes **354.471 seconds**, using
fresh PostgreSQL **17.11**, **149 migrations**, restricted runtime roles and
separate API/Worker processes under strict verified-email policy. All three
application runtime metadata records identify
`5b959b2f0a3607ccb13cc411ccb01c75cec564f7`, version `0.1.0-1736`.
All **1,261 source inputs**, **71 API/Worker binary files**, frontend output and
**four served frontend assets** match their captured hashes. Cached images
supply runtimes with local outputs mounted read-only; this is local execution
evidence, not certification of a current immutable release.

API/Worker identities and start times remain unchanged during both cases.
All owned application/database containers, network and provider thread/socket
are retired; the original Docker inventory is restored and **829 temporary host
bytes** are removed. That byte count excludes ephemeral container data and does
not imply physical Docker disk-file compaction. No image is built, pulled or
imported, the original database and local Demo are unused, and no external email
is sent.

The [CI wrapper](../../scripts/ci/test-core-mail-browser.sh) owns a fresh runtime
JSON report and requires the [report validator](../../scripts/ci/verify-core-mail-browser.mjs)
to accept exactly both executed passes without skips or retries. It also checks
unchanged API/Worker identities and restores only the admitted provider fixture.
The validator accepts the actual native report; the final CI wrapper awaits
exact-image execution. All **10 browser collection/report tests** and **277 CI
guard tests** pass, alongside browser TypeScript, shell syntax and workflow
lint. The additional mandatory stage belongs to `browser-notifications` and
requires the identity-mail fixture. Its six-minute case budget accommodates real
delivery backoff without shortening production retry intervals. Ordinary shard
collection still covers **400 cases in 146 files**; collection alone does not
execute those cases.

These are a few functional HTTP/SQL/browser operations and bounded retry observations,
not a throughput, capacity, latency or load test. Large-load testing remains
deferred until after MVP. Current exact-image/full CI, real provider/deployment
resilience, AI/meeting coverage and remaining
PRD/architecture acceptance still govern closure. ARCH-08 and MVP remain open.
