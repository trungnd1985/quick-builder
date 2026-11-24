---
issue: 50
kind: fixed
breaking: false
config:
  added: []
  changed: []
  deprecated: [Admin:PathPrefix]
  removed: []
background: []
caches: []
manual: ["If you set Admin:PathPrefix (Admin__PathPrefix), remove it: the admin has always been served at /admin, the setting is now ignored, and startup logs a warning while it is set."]
---
`Admin:PathPrefix` is deprecated and ignored; it will be removed in 1.0.0. It never moved the admin, which is served at `/admin` (under `Hosting:BasePath`), but it did change the links in form-notification emails and the set-password links the `identity` commands print, so with any value other than `/admin` those links led to a not-found page. They now always point at `/admin`.
