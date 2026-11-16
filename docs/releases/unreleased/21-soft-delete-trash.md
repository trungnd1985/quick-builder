---
issue: 21
kind: added
breaking: false
config:
  added: [Trash:RetentionDays]
  changed: []
  removed: []
background: [Hourly purge of trashed content past the retention period]
caches: []
manual: []
---
Deleting a page, shared component, collection item or media file now moves it to the site's trash, where members can restore it for 30 days (or as set by `Trash:RetentionDays`) before a background job deletes it permanently.

Only Administrators can delete permanently or empty the trash; Designers and Editors can delete to the trash and restore what they are allowed to delete. Media files in the trash keep using storage until they are purged, so an Administrator can delete them permanently to free the space at once.

Content deleted before this upgrade is not in the trash and cannot be restored. The admin API's `DELETE` of these four kinds now answers 200 with a receipt instead of 204.
