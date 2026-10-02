# Browser recovery CI investigation

Run 37061592501's exact-image browser job completed with five failures. The real
notification-center desktop/phone recovery scenario passed. Failed cases were
archived List return focus at 390px, Card deletion review at both widths, label
creation recovery at 1280px, and phone label-filter opening.

Archived Lists did not preserve the return target when a subsequent Worker-driven
read disabled the refresh button. Its focus restoration now waits for the MUI
dialog to exit and remembers focused refresh controls through background reads.
A regression simulates acknowledged restore, focus return, a pending live read
that loses focus, and restoration after that read; all 22 List archive tests pass.

Card deletion previously focused a possibly disabled primary-page button after
waiting only for the other page's heading. The browser case now waits for its own
live state, enabled review control and verified keyboard focus before Enter.
Label creation's failed DOM still had the color listbox open. That case now waits
for the selected menu to exit, checks the enabled submit control and verifies focus
before Enter. The filter case also explicitly verifies focus before its keyboard
activation. These are condition-based waits, with no skipped scenarios, relaxed
success/retry assertions, repeated mutations or fixed delay substitutes.

A local real-Chromium diagnostic with mocked transport confirmed that label
selection/menu-exit timing overlaps the next keyboard operation; it is diagnostic
evidence only, not a replacement for exact-image integration. The new browser
assertions and List focus repair require a fresh exact-image CI run. The prior
failed required-CI gate remains failed, and no ticket is closed by this change.

Run 37074806985 passed .NET and PostgreSQL source checks but failed one archive
component test: its live invalidation callback was invoked before the subscription
passive effect attached. The test now waits for that callback readiness, keeping
the three invalidations, exact read counts and non-aborted pending read assertions.
This is a test setup correction; it does not weaken the slow-read behavior check.
