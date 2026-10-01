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
