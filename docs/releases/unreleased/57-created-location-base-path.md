---
issue: 57
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
Under `Hosting:BasePath`, the API's `201 Created` responses now give the new resource's address under the base path. Before, creating a site, page, component, menu, collection, item, user, uploaded file or host name answered with a `Location` of `/api/v1/...` instead of `/cms/api/v1/...`, so a client that followed it left the base path.
