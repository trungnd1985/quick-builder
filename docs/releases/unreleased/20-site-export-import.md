---
issue: 20
kind: added
breaking: false
config:
  added: [SiteTransfer:MaxUploadSizeBytes]
  changed: []
  removed: []
background: []
caches: []
manual: ["A reverse proxy limits upload size: raise its limit for /api/v1/transfers/uploads to import large archives in the admin, or use the sites import command, which reads archives from disk and has no limit.", "Exports kept in the admin use media storage space for 7 days.", "Archives hold unpublished content and members' display names. Store them as you store database backups."]
---
Administrators can export a site to one archive and import it as a new site, into an existing site (replacing, skipping or renaming what both hold), or as a restore of the site it came from, which first keeps a copy of the site as it was. The `sites export`, `sites check` and `sites import` commands do the same from the command line, for nightly backups with `sites export --all` (see the operations guide).
