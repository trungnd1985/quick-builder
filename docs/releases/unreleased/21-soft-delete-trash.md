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
Deleting a page, shared component, collection item or media file now moves it to the site's trash. Members can restore it for 30 days, or as set by `Trash:RetentionDays`; after that it is deleted permanently by a background job.

Media files in the trash keep using storage until they are purged. To free space at once, an Administrator can delete them permanently or empty the trash. Only Administrators can delete permanently; Designers and Editors can delete (to the trash) and restore what they are allowed to delete.

Content deleted before this upgrade is not in the trash and cannot be restored. The admin API's `DELETE` of these four kinds now answers 200 with a receipt instead of 204.
