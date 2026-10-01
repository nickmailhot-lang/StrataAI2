# Visibility browser readiness

The db3f477 release run 36926030983 failed three browser cases: desktop/phone
visibility and two-administrator live recovery. Both member administration cases
passed. Source, PostgreSQL, image build and security jobs passed, but the required
gate failed and no release bundle was emitted. This run is not green evidence.

Its retained traces identify the body failure as waiting for the Public option.
In the desktop visibility trace, keyboard ArrowDown began at monotonic time
810349 ms and the visibility bootstrap snapshot request began at 810352 ms.
The subsequent invalidation cleared/remounted the control and closed the menu.
The readiness count had included a late snapshot response from the previous
Board screen. Cleanup then obscured the original timeout with context-close
errors. The failure is not evidence that live delivery remained disconnected:
the trace reached Live visibility updates connected and earlier member cases
passed, including the two-administrator role/reconnect portion.

`trackBoardReads` now associates matching GET requests with the page path when
they start. A response from an earlier screen cannot satisfy visibility readiness.
The three required browser cases still require two successful visibility-page
reads and no busy indicator before consent. They use keyboard activation and
assert the menu expanded before looking for Public, providing a bounded useful
failure instead of waiting through the entire test for a missing option. Browser
contexts close before synchronous Worker restoration, preserving body diagnostics.
Production behavior, assertion deadlines, retries and release gates are unchanged.

A required Node regression simulates previous-screen completion after navigation
and verifies it does not advance readiness. It also checks two current successful
reads do, while failed/aborted reads, another Board and mutation responses do not.
The regression passes locally, and all three browser cases collect. Exact-image
execution of the corrected tests remains pending. No complete PRD-05 or current
main release acceptance claim follows from this repair.

Follow-up executed evidence: run 36933671696 failed four browser cases with 38
passing and one separately covered mail scenario skipped in the general suite.
The three visibility error snapshots contain the open named listbox and its
Private/Organization/Public options. The failed assertion looked for the
background combobox through the accessibility tree; MUI's open modal menu hides
that background. The cases now assert the visible named listbox, retaining
keyboard opening, option selection and consent assertions. The same correction
applies to the destination/position menus in the card-movement browser cases.

The recovery failure trace records no password-forgot POST, and the final snapshot
shows an empty reset form. A navigation/fill race is the working inference: the
login and recovery screens both expose an Email field. The test now waits for
the Reset your password heading after following the link before filling and
submitting. The generic acknowledgment and invalid-token assertions remain.

All 17 affected browser cases collect locally. Execution of these corrections
remains pending Linux CI; deadlines, retries, topology and acceptance gates remain
unchanged. Neither diagnostic snapshots nor collection prove the repair passes.
