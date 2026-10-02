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

Shell syntax and diff checks pass locally. Run
[36943362278](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36943362278)
at 58c1ad4 reports success for job 110640774813 step 36 (exact-image identity and
invitation mail), including this expired-lease boundary, attempt-two completion
and existing no-additional-provider-send assertions. Step 39, dedicated mobile
verification/recovery through Worker delivery, also reports success. Step 33,
concurrent/large-board rank allocation, reports success in the same job.

At the evidence check, authenticated browser E2E was still running. This is
step-level runtime evidence; final decoded logs, required-ci, release artifacts
and full acceptance still need audit. It neither proves the root cause of the
historical failures nor establishes a fully green release. No mail or
architecture issue is complete from this evidence alone.
