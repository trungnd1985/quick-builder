Every storage setting is checked at startup; an unusable one stops the app before it
listens, with a message that names the setting.

**S3-compatible services** include AWS S3, Cloudflare R2, MinIO and Ceph: any service that
supports conditional writes (`If-None-Match: *`). Startup checks that it does.

**Startup and readiness.** With a remote provider, startup writes, re-writes (which must be
refused), reads and deletes a small probe object under `{KeyPrefix}.quickbuilder-probe/`,
within 30 seconds. A failure stops the app before it listens and names the setting at
fault: credentials rejected, bucket or container missing, access denied, unreachable, or no
conditional writes. `/health/ready` includes a storage check (2 seconds for a remote
provider): an instance whose storage stops being writable or readable after startup reports
not ready until it recovers.
