# Dependency locking (ARCH-01 / ARCH-11)

The root npm lockfile locks the web workspace and browser test dependencies.
Each .NET project commits `packages.lock.json`, including projects without direct
package dependencies. NuGet lockfiles record the resolved dependency graph and
package content hashes; central requested versions remain in
`Directory.Packages.props`.

CI restores the solution with `dotnet restore StrataAI2.slnx --locked-mode`.
Both API and Worker Docker build stages copy their projects' lockfiles before
running locked restores. A manifest/lock mismatch fails rather than silently
resolving another package. Publishing uses the already restored graph. Dependency
audits also begin with a locked restore. No new runtime process is introduced.

For an intentional dependency update, edit the central versions, run
`dotnet restore StrataAI2.slnx --force-evaluate`, review all changed lockfiles and
commit them with the manifest. Then run locked restore and the relevant builds,
tests and security checks. Never fix a lock failure by disabling locked mode in
CI or Docker. Use `npm ci` for the committed web graph; update the npm lockfile
explicitly when changing web dependencies.

CI's `test-dependency-locks.sh` changes a requested package version in a disposable
copy and uses an empty package source. It requires NuGet's lock-mismatch error
`NU1004`, proving that dependency drift cannot be silently accepted or repaired
by fetching a replacement package. The original checkout is not modified.

This locks package dependencies, not the operating system, SDK or container base
images. The .NET 10 SDK feature roll-forward policy remains in `global.json`;
the documented Node/.NET/Docker toolchains are still required. Release CI builds
each image once and tests/emits those exact images.
