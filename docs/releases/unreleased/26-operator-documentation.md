---
issue: 26
kind: added
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
Operator documentation is now a task-oriented site in `docs/operators/`, published for each release: install, configure, reverse proxy and TLS (Nginx, Caddy, Traefik), back up and restore, upgrade, troubleshooting and a security checklist. The configuration reference is generated from the product itself, and every release checks that each setting is documented and that the install, proxy and backup procedures work as written. Startup refusal messages now link to the published upgrade guide for the running release.
