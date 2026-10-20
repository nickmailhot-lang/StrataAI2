# Retained source-free runtime rehearsal

This audit covers `ARCH-11-FR-052`, `ARCH-11-FR-082`, and `ARCH-11-AC-012`. It preserves the six existing slice, audit, and test identifiers. The authenticated PR113 bundle, artifact `12215108146`, matches 518,910,311 bytes and SHA-256 `4f3c797d76795a9afb61aaabcb50bb7be2864eb654b7420a3981550bd4fa828e`. Its 196 safe archive entries and 195 file checksums were verified. All three retained images loaded without rebuilding the application.

The original full source-free driver passed:

- Packaged migrations and restricted Production database roles.
- Readiness, proxy behavior, and exact image identity.
- Database outage refusal and recovery.
- Graceful API/Worker shutdown and restart with the same containers and images.
- Runtime password rotation and rejection of the old passwords.
- Acceptance-record identity, refusal while the Worker is unavailable, and recovery using the same image.

Cleanup removed the rehearsal's containers, volume, network, and temporary settings. The unrelated Worker's identity and start time were unchanged. Private logs and temporary credentials are not part of the delivery evidence package.

Runtime commands use only the packaged payload and retained images. The host has development tools installed, but no application runtime was built from source. This rehearsal does not certify a separate physically clean machine, a real two-version rollback, managed providers, the current PR115 candidate, or the entire MVP. The new audit evidence itself must be qualified and integrated before its verification records can be Done.

The linked native workflow history retains all three failed aggregates and the final eight-case Production pass. Local fixtures use fresh owned PostgreSQL, an owned fake identity transport, localhost Secure cookies, and Organization-scoped Worker fixtures. Production deployment and real provider credentials remain outside this rehearsal.
