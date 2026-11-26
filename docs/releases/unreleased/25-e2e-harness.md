---
issue: 25
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
In the trash, each entry's "Delete permanently" button is now announced by screen readers as "Delete permanently: <name>", so its spoken name starts with the words shown on it, as voice control software expects (WCAG 2.5.3). It was announced as "Delete <name> permanently", which a member saying "click Delete permanently" could not reach.
