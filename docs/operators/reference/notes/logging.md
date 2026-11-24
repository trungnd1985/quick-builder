Logs are structured JSON on standard output. Every entry carries a `TraceId`, the same value
returned in the `traceId` of an error response, so "it failed, here is the id" is enough to
find the entry. Logs never contain credentials, secrets, personal data or content bodies.
