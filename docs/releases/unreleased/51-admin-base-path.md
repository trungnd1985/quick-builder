---
issue: 51
kind: fixed
breaking: false
config:
  added: []
  changed: []
  deprecated: []
  removed: []
background: []
caches: []
manual: []
---
The admin now works under `Hosting:BasePath`. Before, with a base path such as `/cms`, the admin page at `/cms/admin/` still loaded its scripts from `/admin/`, called the API at `/api/v1` and opened previews at `/_preview`, all outside the base path. Behind a proxy that forwards only `/cms`, it did not load at all. It now uses addresses under the base path, including the set-password links it shows.
