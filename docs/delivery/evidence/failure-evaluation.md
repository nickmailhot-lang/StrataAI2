# Development failures retained — 2026-10-17

Owner: delivery implementation owner. All runs below occurred on uncommitted development inputs before the frozen implementation revision. They are historical diagnostic evidence, not passing results for the baseline commit or current release candidate. The later frozen-source results are recorded separately in progress.json.

Retained text outputs normalize line endings and trailing whitespace for portable review. Their diagnostic content is preserved; recorded SHA-256 values refer to the retained artifact bytes. `.gitattributes` fixes evidence text to LF across operating systems.

| Evidence | Cause and impact | Resolution |
| --- | --- | --- |
| initial-31-of-32.txt | The incomplete-child fixture reset the child item but retained its verified criterion. The validator correctly refused stale acceptance before reaching the intended required-child guard. Test fixture failure; no application/runtime change. | Reset the corresponding child criterion as well. Preserve the required-child assertion and every validator rule. |
| decomposition-refusal.txt | Separating password recovery from deactivation temporarily left a Test pointing at the retired slice-specific acceptance key. Baseline validation refused the orphan trace. The expanded Test-pass guard also exposed two gate fixtures whose earlier refusal prevented reaching their intended gate assertions. | Move the Test trace to the new password recovery child; retain deactivation's own trace. Target the gate-refusal fixtures at the Task so they exercise mandatory gates independently of the Test-pass guard. |
| test-pass-refusal-32-of-34.txt | After the trace correction, both remaining fixture failures correctly encountered the new unsubstantiated-Test-pass guard before the intended missing/waived gate. | Move those gate mutations to the Task; assertions and all missing-gate cases remain. Add independent tests proving unsubstantiated Test passes and initial-Done bypasses are refused. |

No test was skipped, relaxed, retried automatically or removed. No existing suite or performance/capacity threshold changed. The final delivery suite contains 34 positive/refusal contracts. The 330 existing metadata, dependency, integration-coverage and browser-shard contracts also passed during development; the applicable suites are rerun at the frozen source revision before claiming current evidence.
