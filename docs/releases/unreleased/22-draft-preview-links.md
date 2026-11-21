---
issue: 22
kind: added
breaking: false
config:
  added: [Preview:LinksValidFrom]
  changed: []
  removed: []
background: [Hourly sweep of preview links that ended more than 30 days ago]
caches: []
manual: [Forward /_preview/ through reverse proxies that only pass selected paths]
---
Members can preview any draft page or collection item, or any kept past version, rendered exactly as it will be published, and share a time-limited link with someone who has no account. Links last 7 days by default and 30 days at most, and members can revoke them; Administrators see and revoke every link of a site.

Previews are served under `/_preview/` on the site's host name and on the admin host name. If your reverse proxy forwards only selected paths, forward `/_preview/` too.

To make every existing preview link stop working at once, set `Preview__LinksValidFrom` to the current time and restart.
