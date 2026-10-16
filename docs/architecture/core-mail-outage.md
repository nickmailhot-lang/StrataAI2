# Core task work during a mail-provider outage

This is partial [ARCH-08-AC-003](https://github.com/nickmailhot-lang/StrataAI2/issues/89)
evidence for operational task management. Mail failure must leave existing
verified users able to work with their protected Boards, Lists and Cards.
Pending identity mail remains durable and retries through the separate Worker.
The wider AI/meeting, browser, managed-provider and complete release acceptance
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

These are a few functional HTTP/SQL operations and bounded retry observations,
not a throughput, capacity, latency or load test. Large-load testing remains
deferred until after MVP. Current exact-image/full CI, native browser outage
behavior, real provider/deployment resilience, AI/meeting coverage and remaining
PRD/architecture acceptance still govern closure. ARCH-08 and MVP remain open.
