# Organization review and removal recovery verification

Retained `eea64913` browser shard 3 fails desktop configuration review and
membership re-review. The configuration trace has no protected configuration
GET after Enter; the membership trace has a successful member GET after Enter,
but no displayed current-role review in the final page snapshot. Test-source
text below that snapshot is not evidence of visible UI. A separate `32cda04f`
trace fails the phone original-removal acknowledgment assertion.

The unchanged complete local six-case baseline passes 4/6 in 361,645.248 ms.
Desktop removal uncertainty disappears during background checking; phone
recovery loses the explicit current-membership action after acknowledgment.
These reproduce recovery-state defects, without claiming identical timing to
every hosted failure. Two component regressions fail against the old code.

Live refresh still withdraws member details and consent and checks current
account/access. It now preserves the target identifier for an explicit fresh
review, including after original-removal acknowledgment. Background checking
preserves the uncertainty message only while an original retry is pending.
Access withdrawal still clears private state and recovery; no automatic removal
or stale consent is admitted.

An intermediate complete run passes 3/6 in 377,405.216 ms. Both membership
re-reviews receive HTTP 200 but live invalidation retires the review; the desktop
retry sends no second DELETE after Enter. A third component regression confirms
that invalidating an in-flight read lost its explicit re-review action. It fails
before the additional repair and passes afterward. The browser re-review now
uses the existing five-second read-only opening helper. Original removal retry
admits enabled focus before its single Enter activation; no mutation is retried
inside an opening loop. Both original DELETE count/key/path checks remain.

Configuration review observes the original dialog through the same bounded
keyboard opening helper. A subsequent complete run passes 5/6 in 371,677.296 ms;
all four membership cases pass, while phone configuration history receives
HTTP 200 but its displayed revision is withdrawn during live admission. The
history step now observes Revision 1 within a five-second read-only opening
window, repeating only the original pointer click when the launcher is enabled.
Approval writes, history fields and heading focus checks remain unchanged.

The final complete invocation of the three original repository files passes
**6/6 in 349,952.071 ms**, with zero failures, skips or flaky cases. Original
30-second configuration/member-administration and 240-second live-membership
scenario deadlines remain. Cancel/confirmation focus, desktop/phone layouts,
live peer reconciliation, lost acknowledgments, rejoin recovery, exact original
keys and command counts, canonical membership preservation, and accessibility
assertions remain. Incomplete earlier captures are excluded from final passing
evidence.

All **40** member-administration and metadata-stream component checks pass.
Web/browser types, lint, the web build, fresh solution build and **213** immutable
CI guards pass. Independent verification finds zero mismatches across **2,101**
hosted source/runtime hashes, **1,414** native source hashes and four web assets.
Shared read-only compiled builds avoid redundant runtime copies. Owned
containers, temporary database/roles and credentials are removed; original
stopped PostgreSQL data is preserved.

The local fixture uses real restricted PostgreSQL/RLS, API, separate Worker and
browser transport, with optional API email verification and verified-account
Worker policy. Its isolated identity-mail Worker is disabled, so this is not
managed-mail acceptance or proof of every hosted timing detail. Mounted builds
do not certify retained immutable release images. Current complete build-once
CI and the full [PRD-03](prd-03-acceptance.md) and
[PRD-27](prd-27-acceptance.md) acceptance maps remain required for closure.
Estimated work remaining stays **8% for PRD-03** and **24% for PRD-27**;
both issues remain open.
