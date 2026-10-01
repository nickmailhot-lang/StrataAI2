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
