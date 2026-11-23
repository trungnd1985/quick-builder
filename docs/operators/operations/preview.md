# Preview links

Members preview drafts and past versions from the admin, and can share a time-limited link
with someone who has no account.

someone who has no account. Previews are served under `/_preview/`, on every host name: the
site's own host names and the admin host names alike. If your reverse proxy forwards only
selected paths, forward `/_preview/` too, on the host names where links should open (a site's
primary host name, or, for the admin's own previews, the admin host name).

Previews are never cached, never indexed (`X-Robots-Tag: noindex, nofollow`) and never
send their address on (`Referrer-Policy: no-referrer`). Their paths are never written to the
logs, because a link's path holds its secret.

## Ending every preview link

If a list of preview links leaked, or you simply want every link to stop:

1. Set `Preview__LinksValidFrom` to the current UTC time, for example
   `Preview__LinksValidFrom=2026-11-21T09:30:00Z`.
2. Restart the application.

Every link created before that time now answers "This preview link is no longer available".
Nothing else changes, and members can create new links straight away. Administrators can also
revoke the links of one site from **Sites → (site) → Preview links**.
