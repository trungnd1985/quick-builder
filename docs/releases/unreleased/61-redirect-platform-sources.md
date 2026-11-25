---
issue: 61
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
A redirect from an address under `/_forms` or `/_preview` is now refused, as one under `/_media` or `/_assets` already was. The platform answers those addresses itself, so such a redirect was saved but never fired. Any you created before still never fire, and you can delete them.
