# Invitation mail recovery fixture

Release runs 36939590070 and 36940792630 failed while waiting for generic job
completion after the invitation delivery ledger was already SENT. Their logs
were retained after cleanup restored the Worker, so the failing job state was
not recorded. Commit 9dcdff0 adds constrained pre-cleanup failure diagnostics.

Source inspection identifies a scheduling race in the former fixture: it reset
the generic job to PENDING while the Worker was still running, then restarted
the Worker. A claim made between those operations can leave a fresh two-minute
lease on shutdown. The existing ninety-second polling window cannot reclaim
that lease. This explains a possible failure path, not a proved diagnosis of
those historical runs.

The fixture now stops the Worker before establishing the simulated crash
boundary: a RUNNING job with one attempt and an expired lease, while its
delivery ledger remains SENT. It asserts that boundary, starts the same scoped
release Worker, and requires SUCCEEDED on attempt two with no remaining lease.
Existing provider-attempt and single-message assertions still require no
additional send. The polling window is unchanged; runtime role, tenant scope,
delivery admission and immutable release images are unchanged.

Shell syntax and diff checks pass locally. Execution requires Linux CI with
PostgreSQL and the exact release API/Worker images; runtime success remains
pending. No mail or architecture issue is complete from this fixture change.
