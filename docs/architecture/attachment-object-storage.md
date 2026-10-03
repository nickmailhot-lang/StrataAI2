# Attachment object storage decision and provider contract

Decision: use managed AWS S3 as the initial production object-storage adapter.
ARCH-07 explicitly adopts managed production object storage behind Application
interfaces, permitting MinIO/filesystem locally. This selects the previously open
initial provider within that adopted topology; it adds no database, broker or
deployable application service. `IAttachmentObjectStorage` and Domain remain
vendor-independent. The explicit local filesystem adapter remains available for
local/tests, with no production fallback. The decision implements ARCH-07-FR-001,
FR-010/FR-012 and PRD-14-FR-004, subject to the remaining integration below.

The Infrastructure adapter uses official `AWSSDK.S3` 4.0.104.1 (and its locked
`AWSSDK.Core` dependency). Credentials must come from runtime workload identity or
runtime secrets when the client is registered; no credentials enter settings,
images, metadata or logs. Regional HTTPS AWS endpoints are required. Custom
endpoints, HTTP, access-point ARN buckets and ambiguous dotted bucket names are
not supported by this adapter; MinIO requires a separately configured local
adapter. Expected bucket owner is a required 12-digit account ID and is included
in every request. This implementation never creates or changes a bucket.

Before each byte operation, the adapter requires all four bucket public-access
blocks, an explicit non-public bucket policy status and exactly one
BucketOwnerEnforced ownership rule. Missing/inaccessible/unsafe configuration
fails closed with fixed `object_storage_unavailable`, before upload/download/
deletion. The runtime identity needs the read permissions for these checks.
The operator must provision a private general-purpose bucket, an explicit private
policy, least-privilege prefix access and abort-incomplete-multipart lifecycle
rules. Administrative bucket settings are a trusted operator boundary: a
configuration check is not a defense against an administrator making the bucket
public while an operation is already running.

Keys derive only from server-owned Organization/attachment UUIDs. Multipart
upload uses one 5 MiB buffer, reads at most one byte beyond the server's positive
limit (maximum 1 GiB), measures actual bytes and SHA-256 and accepts non-seeking
sources. Parts are uploaded privately with server-owned application/octet-stream
and SSE-S3 AES256; no filename, client MIME, ACL or public URL is supplied.
Completion includes expected byte count and `If-None-Match: *`, so the managed
provider refuses replacement of an existing identity. Part ordering/ETags are
collected from SDK acknowledgements. Caller owns the source stream. Private reads
wrap the SDK response so closing the returned forward-only stream retires the
response/transport as well as its bytes. Missing keys are distinguished only
inside the already-admitted application workflow.

AWS documents [conditional multipart completion](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TCompleteMultipartUploadRequest.html),
[initiation and encryption](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TInitiateMultipartUploadRequest.html)
and the [official SDK installation boundary](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/net-dg-install-assemblies.html).
The SDK handles embedded errors in HTTP-200 completion responses. Transient/
conditional-conflict failures use fixed unavailable outcomes; a definitive 412
refuses an existing object. Application original-intent/idempotency remains
required above storage and must not interpret a provider timeout as no write.

Failed/empty/oversize/cancelled uploads attempt a separately bounded 10-second
abort of their multipart ID. Aborting parts never compensates by deleting the
final key: completion may have succeeded before an acknowledgement timeout.
Cleanup outage/crash can retain unpublished parts; provider lifecycle and durable
orphan reconciliation are required. Deletion removes normal current-key access;
in a versioned bucket, provider retention/version cleanup is separate lifecycle
work. This primitive does not authorize a user, publish a clean verdict or clear
a cover. Requests need bounded caller cancellation/provider timeouts when wired
into the upload and Worker workflows.

Fifteen initial adapter cases plus endpoint/privacy refinements compile against
the real vendor SDK types using a substituted SDK client. They cover multi-part
non-seeking byte/digest measurement, expected owner/scope on all calls, source/
response ownership, duplicate identity, commit-with-lost-reply preservation,
empty/oversize rejection, all four public-access flags, public policy, ownership,
privacy outage, source/part failure, cancellation and invalid limits/configuration.
These are adapter contract tests, not executed real-bucket or transport proof.
Locked restore, warning-as-error build and dependency vulnerability audit pass;
Linux execution and immutable-image security gates remain pending.

Remaining integration: runtime provider/client registration and readiness,
credential/region/prefix policy diagnostics, managed-bucket transport acceptance,
file upload authorization/admission/type inspection/idempotency and compensation,
durable metadata/digest/scan-job publication, scanner provider, Worker lease/CAS/
outbox, controlled delivery, preview/cover, tombstone/retention reconciliation and
the complete security/browser/accessibility/performance matrix. No binary upload
or download endpoint is enabled by this adapter alone. No ticket is closed.
