---
issue: 23
kind: added
breaking: false
config:
  added: []
  changed: []
  removed: []
background: []
caches: []
manual: ["Each open editor or item form sends a small request every 10 seconds; a reverse proxy that rate-limits the admin API must allow it.", "Reload admin tabs left open from before the upgrade to get presence and locks; until then their saves are still protected against overwriting."]
---
The editor and the item form show who else has a page, component or item open. The first person to change it holds it, and others see it read-only, with who and since when, until it is free or idle for 30 minutes, when they can take it over. A save refused because someone else saved first says who and when, and offers to reload or keep the version as an unpublished copy. Publishing an item no longer refuses someone else's next draft save.
