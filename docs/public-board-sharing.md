# Public Board link

The visibility administration page exposes a labeled, read-only, keyboard-selectable
absolute link and an Open public Board action only after an authorized canonical
read reports PUBLIC visibility. Selecting Public in the draft or opening consent
does not expose the link. The URL uses the current application origin and the
existing scoped Board route; it contains no access token or invitation proof.

The description explains that anyone with the link can read the Board and editing
requires separate permission. Opening the link uses a separate tab with opener
isolation. Browser-standard copy/link operations remain available without requiring
clipboard permission. Narrowing visibility, losing administration, an invalid scope,
or a failed canonical read clears the link together with the protected Board state.
Sharing a URL does not freeze visibility or confer authorization: every subsequent
read/mutation continues through server-side policy. The URL itself is not a grant.

The existing Board screen admits anonymous visitors through the same scoped API
and renders controls from the authoritative access response. No alternate datastore,
public permission bypass or Owner Portal relationship is introduced.

Seven visibility component cases pass locally, including draft/confirmation gating,
the read-only field and removing a public link on canonical narrowing/read failure.
Lint and production build pass. The required desktop/phone visibility browser cases
now open the actual shared URL in a fresh anonymous context and assert the Board is
visible while list creation and Board administration links are absent. They also
retain direct API read-only/unauthenticated mutation assertions. Both cases collect
locally; release-image execution remains pending. This evidence does not establish
full PRD-05 acceptance, large-data latency or complete accessibility conformance.
