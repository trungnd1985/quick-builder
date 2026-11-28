---
issue: 68
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
On the Design screen, clicking **Publish design** while a save was still in progress could report "Someone else changed the design tokens" even when no one else was editing, and the design was not published. Publishing now waits for that save to finish and publishes what it stored.
