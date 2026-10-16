# Negative security fixture assertions

Standalone `! grep` is exempt from Bash `set -e`. A forbidden match can therefore
invert the command status without stopping the surrounding security script.
Private discovery, failed mutation, retry response and event replay checks now use
`scripts/ci/assert-file-excludes.sh` as a normal command.

The utility accepts an extended regular expression and one fixture path. It returns
0 only when inspection succeeds and finds no forbidden content; a match returns 1,
and an invalid pattern, missing file or inspection failure returns 2. Diagnostics
never include the matched fixture contents. Proper conditional `if ! grep` checks
for expected presence retain their existing behavior.

The required web-quality job executes `test-security-assertions.sh`. It verifies
clean and forbidden files, a filename containing spaces, missing files, malformed
patterns, an empty matching pattern, diagnostic redaction, and a caller using
`set -euo pipefail` that must stop before its release-success marker. Exact-image
integration then exercises the actual privacy checks using the same utility.

This hardens test failure behavior; it is not evidence that every security,
realtime, portal, search/AI or lifecycle requirement is implemented. The associated
PRD and architecture tickets remain open until their complete criteria pass.

## Code-scanning follow-up — 2026-10-15

A fresh main-branch CodeQL alert inventory contains nine open findings. Review
the actual source and reported flow before classifying a finding; an alert's
severity alone does not establish a production credential disclosure.

| Alerts | Inspected scope | Action and acceptance boundary |
| --- | --- | --- |
| [13](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/13) | Worker structured logging; both flows originate at the `IdentityTokenPurpose.ResetPassword` enum constant | Dismissed individually as a false positive after confirming the typed job category contains no password/token/proof. Logging, the query and scanning remain enabled. |
| [7](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/7), [8](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/8), [9](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/9), [10](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/10), [11](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/11) | Five temporary credential-file write paths in the identity-role and List-move PostgreSQL fixtures | Removed file materialization; Docker inherits named values from each child process environment. Fresh CodeQL analysis must establish closure. |
| [3](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/3), [6](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/6), [12](https://github.com/nickmailhot-lang/StrataAI2/security/code-scanning/12) | Configuration test selectors escape only parentheses before constructing regular expressions | Escape the full set of regex metacharacters, preserving literal label matching. Fresh CodeQL analysis and retained-image browser execution remain required. |

The dismissed finding points at [the guarded log sink](../../src/StrataAI.Worker/WorkerLog.cs).
Its source is [the two-value purpose enum](../../src/StrataAI.Application/Identity/IIdentityDeliveryTokenSigner.cs),
carried by [the typed delivery job](../../src/StrataAI.Application/Identity/IIdentityDeliveryStore.cs)
and logged as a category by [identity diagnostics](../../src/StrataAI.Worker/IdentityEmailWorker.cs).
The flagged path is unchanged between its analyzed `5bb5dffd` revision and the
review's `a45dad51` parent. This disposition covers only that reported enum flow;
it does not exempt the sink from future credential/privacy review.

Both PostgreSQL fixtures still generate independent random credentials for
their new isolated containers, but pass only environment-variable names in
Docker arguments and values in the Docker child's environment. Runtime and
negative provisioning overrides use the same mechanism. Neither fixture writes
`server.env`, `client.env` or `provision.env`; cleanup asserts absence of `.env`
files and still removes its owned temporary directory even when that assertion
fails. This avoids both local credential files and plaintext password arguments.
It does not change deployed credential providers or claim that runtime process
environments are inaccessible to a privileged host operator.

The **three original real-PostgreSQL tests** pass in **37.369 seconds**, including
quoted/empty/reused/short credential handling, actual restricted-role access,
reprovisioning, source/receipt rollback and List/Card parent integrity through
the real upgrade. Both use fresh network-isolated PostgreSQL/pgvector containers
and small functional data. All **156 captured source/database inputs** match
afterward, and each fixture restores original Docker inventory. Credentials stay
in child/container environments until their processes and owned containers retire;
no existing local database/service or new image build/pull/import is used.

All **18 configuration component tests**, web/browser typechecks and focused
lint pass. Full CI, actual new CodeQL results and complete ARCH-06/MVP acceptance
remain required. The eight source-corrected alerts are not manually dismissed or
claimed closed; only the independently verified enum false positive is dismissed.
No scan rule, security gate, functional assertion or load-phase policy is disabled.
