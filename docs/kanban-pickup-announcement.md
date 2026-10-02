# Keyboard pickup instructions

Run 36943362278 at 58c1ad4 failed both list keyboard browser cases while waiting
for the pickup instruction. Its PostgreSQL ranks and isolated Worker mail steps
passed. Its separate card timestamp failure predates the persisted-baseline fix.

The announcer previously replaced pickup instructions when the initial collision
reported the source itself. It now returns the same pickup instructions for
that starting position for lists and cards. New targets retain their named
position messages; cancellation and drop messages retain their existing meaning.
This removes an observable overwrite path without removing the browser assertions.
Three focused announcement tests, typecheck and lint pass. Exact-image keyboard
execution is pending; this source diagnosis does not prove a screen reader run.
