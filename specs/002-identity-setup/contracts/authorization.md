# Contract: Declaring and Enforcing Authorization

**Feature**: `002-identity-setup` | **Audience**: anyone adding an endpoint to any module

This contract is binding on every later module. Before this feature, 001's
[module-contract.md](../../001-foundation-skeleton/contracts/module-contract.md)
defined how a feature slice is built. This document adds the one rule that slice
lacked: **every action declares who may call it**. The build fails otherwise.

---

## 1. Every action carries exactly one declaration

| Declaration | Meaning | Typical use |
|---|---|---|
| `[RequireCapability("<module>.<noun>.<verb>")]` | A signed-in user who holds this capability, at this site if the capability is site-scoped | Almost everything |
| `[RequireSignedIn]` | Any signed-in user. The handler narrows the result itself. | Self-service (own password, own sessions); lists filtered per user |
| `[AllowAnonymous]` | No session needed | **Only** the actions in §4. Anything else fails the build. |

An action with no declaration still fails closed at runtime (the fallback policy
requires a signed-in user). The architecture test fails the build first
(research R-06).

`RequireCapability` and `RequireSignedIn` come from `QuickBuilder.Shared.Kernel`. Module
code never references ASP.NET Core authorization types for this purpose.

## 2. Capabilities

A capability is a named permission, registered by the module that owns the operation,
from inside its `Add<Module>Module()`:

```csharp
services.AddSingleton(new CapabilityDefinition(
    "sites.host_names.manage", CapabilityScope.Site, GrantedTo: []));
```

Naming and scope rules:
- **Naming**: `<module>.<noun>.<verb>`, lower-case, dot-separated. A name never changes
  once shipped, because it is part of the stored meaning of roles in later releases.
- **Scope**: `Installation` or `Site`. A site-scoped capability **must** sit on a route
  with a `{siteId}` parameter, from which the site is read. The architecture test checks
  this.
- **Who holds it**: `GrantedTo` lists the site roles that hold it. **Administrator holds
  every capability everywhere** and is never listed.
- **Duplicates**: registering two definitions with the same name fails at startup.

### Registered by this feature

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `identity.users.manage` | Installation | ✓ | — | — |
| `sites.create` | Installation | ✓ | — | — |
| `sites.view` | Site | ✓ | ✓ | ✓ |
| `sites.host_names.manage` | Site | ✓ | — | — |
| `sites.settings.manage` | Site | ✓ | — | — |
| `sites.availability.manage` | Site | ✓ | — | — |

Later modules register their own capabilities the same way. Media's four
(`media.files.view`, `media.files.upload`, `media.files.delete`,
`media.own_files.delete`) are listed in
[003 research M-18](../../003-media-storage/research.md#m-18-the-four-capabilities-and-the-delete-own-check).
The two Sites capabilities added by 009, `sites.settings.manage` and
`sites.availability.manage`, are explained in
[009 research S-12](../../009-sites-hostname-resolution/research.md#s-12-capabilities).

Pages (010) registers four, following its clarification that the Designer builds and
the Editor writes. They are explained in
[010 research P-13](../../010-page-tree-components/research.md#p-13-capabilities).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `pages.content.view` | Site | ✓ | ✓ | ✓ |
| `pages.structure.manage` | Site | ✓ | ✓ | — |
| `pages.page_drafts.edit` | Site | ✓ | ✓ | ✓ |
| `pages.components.manage` | Site | ✓ | ✓ | — |

The editor core (011) adds no Pages capability:
- holding `pages.page_drafts.edit` allows every editor action on a page's draft
  (011 clarification Q1);
- the token usage count (`GET …/token-usages/{tokenId}`) needs `pages.content.view`.

The Editor module (011) registers two, for a site's design tokens. They are explained in
[011 research ED-20](../../011-editor-core/research.md#ed-20-authorization).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `editor.design_tokens.view` | Site | ✓ | ✓ | ✓ |
| `editor.design_tokens.manage` | Site | ✓ | ✓ | — |

Publishing (012) adds two, registered by Pages. Designers publish and Editors prepare drafts
(012 clarification Q3); the site publish log is for Administrators only. The Editor module
requires `pages.publishing.manage` too, for publishing the design. They are explained in
[012 research PR-18](../../012-publishing-rendering/research.md#pr-18-authorization).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `pages.publishing.manage` | Site | ✓ | ✓ | — |
| `pages.publish_log.view` | Site | ✓ | — | — |

Reads of publishing history and versions use `pages.content.view` (pages, components) and
`editor.design_tokens.view` (the design). Copying a version to the draft uses
`pages.page_drafts.edit` or `pages.components.manage`.

The media library, variants and remote storage (013) add one, registered by Media, for the
storage consistency check. It reports on every site's files, so it is installation-scoped
and Administrator-only. It is explained in
[013 research MV-21](../../013-media-library-variants/research.md#mv-21-the-storage-consistency-check).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `media.storage.check` | Installation | ✓ | — | — |

Everything else 013 adds reuses existing capabilities (013 research MV-24):
- **Folders, moving files and alternative text** use `media.files.upload`.
- **Replace** declares `media.own_files.delete`, with the same row checks as delete:
  another person's file needs `media.files.delete`.
- **Live use**: deleting or replacing a file that a currently published page or component
  uses also needs `pages.publishing.manage` on the site (013 clarifications).
- **Usage places** are named only to holders of `pages.content.view`.

Collections and the schema builder (014) add two, registered by Cms. Administrators and
Designers shape the content model; Editors, who write content, see it. They are explained in
[014 research CS-15](../../014-cms-collections-schema/research.md#cs-15-authorization).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `cms.collections.view` | Site | ✓ | ✓ | ✓ |
| `cms.collections.manage` | Site | ✓ | ✓ | — |

The schema preview is read-only but shows item titles, so it needs `cms.collections.view`.

### Registered by 015

Collection items (015) add two more, also registered by Cms. Every site role writes and
publishes items (015 clarification Q1), unlike pages, where Editors only prepare drafts. The
two are kept apart so that who may change what visitors see can be separated later without
renaming a shipped capability. See
[015 research IT-16](../../015-cms-items/research.md#it-16-authorization).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `cms.items.edit` | Site | ✓ | ✓ | ✓ |
| `cms.items.publish` | Site | ✓ | ✓ | ✓ |

- **Reading** items (list, item, history, referrers, slug availability, lookup) needs
  `cms.collections.view`.
- **`cms.items.edit`**: create, save the draft, duplicate, delete, copy a version into the draft.
- **`cms.items.publish`**: publish, unpublish, schedule and cancel schedules, roll back.
- **Media in live use**: a file a published item uses counts as in live use, so deleting or
  replacing it still needs `pages.publishing.manage` (013 rule, unchanged; 015 IT-15).

### Notes from 016

Collection templates and data binding register no capability; they reuse existing ones
([016 research TB-17](../../016-collection-templates-binding/research.md)):

- **Making a page a template**, changing its collection or turning it back, and the impact
  check before it: `pages.structure.manage` (Administrators and Designers), like every
  other change to the page tree.
- **Binding blocks and placing collection lists** are part of the page or component
  document: `pages.page_drafts.edit` or `pages.components.manage`, as before.
- **The list preview** (`POST /api/v1/cms/sites/{siteId}/list-preview`), which the editor
  canvas calls, is a Cms endpoint that asks for `pages.page_drafts.edit`, named in
  `Pages.Contracts.PageContentCapabilities.DraftsEdit`: only people editing a page or a
  component need it, and Designers, the only role that edits components, hold it.
- **Deleting a collection** still needs `cms.collections.manage`; it is now also refused
  while pages or components use the collection.

### Registered by 017

SEO, sitemap and redirects (017) add three: one registered by Pages, two by Sites.
Designers shape the site's addresses and how it presents itself. Only Administrators may
take the whole site out of search or change where its host names lead, because a mistake
there removes the site from search engines. See
[017 research SE-03](../../017-seo/research.md#se-03-who-may-change-which-metadata).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `pages.redirects.manage` | Site | ✓ | ✓ | — |
| `sites.search.manage` | Site | ✓ | ✓ | — |
| `sites.search_visibility.manage` | Site | ✓ | — | — |

- **`pages.redirects.manage`**: list, create, change, turn on or off, delete, import and
  export redirects.
- **`sites.search.manage`**: title suffix, default sharing image, organisation name and logo.
- **`sites.search_visibility.manage`**: "Discourage search engines", custom robots.txt rules,
  "Redirect other host names to the primary one".
- **Page metadata** is part of the draft (`pages.page_drafts.edit`). Changing the canonical
  address or hiding a page from search engines also needs `pages.structure.manage`, checked on
  the draft save (`403 pages.seo_restricted`).
- **Redirect offers** that come with a change (a move, a slug change, an item publish) need only
  the capability of that change, so an Editor publishing an item slug change may accept one.

### Registered by 024

Navigation and menus (024) add one, registered by Pages. Menus shape the site's structure, so
Designers build them ("Designer builds, Editor writes", 010). See
[024 research NV-18](../../024-navigation/research.md#nv-18-capabilities-audit-and-concurrency).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `pages.menus.manage` | Site | ✓ | ✓ | — |

- **`pages.menus.manage`**: create, rename, duplicate and delete menus, save their drafts, and
  copy a published version into the draft.
- **Viewing** menus, their preview, usages and references needs `pages.content.view`, so Editors
  can see menus and choose one in a Navigation block of a draft they may save.
- **Publishing** menus (publish, unpublish, schedule, roll back) needs
  `pages.publishing.manage`, as for pages and components.

### Notes from 023

Concurrent editing safety (023) adds no capability; it checks the ones that already decide who
may view and change each kind of document ([023 research CE-16](../../023-concurrency/research.md#ce-16-capabilities-by-kind-without-new-capabilities)).

- **Editing sessions** (`/api/v1/editor/sites/{siteId}/editing-sessions…` and `…/editing`)
  declare `pages.content.view`, which every site role holds. The handler then requires the
  document kind's own capability: viewing a page or component needs `pages.content.view`, an
  item `cms.collections.view`; claiming, taking over or moving a lock needs
  `pages.page_drafts.edit`, `pages.components.manage` or `cms.items.edit`. A session acts only
  for its own user.
- **Copies** declare the create capability of their kind: `pages.structure.manage` for a page
  copy, `pages.components.manage` for a component copy, `cms.items.edit` for an item copy.

### Registered by 019

The release pipeline and upgrade safety (019) add one, registered by Identity. Version numbers
help an attacker, so only Administrators see the running release
([019 installation-version.md §4](../../019-release-upgrade-safety/contracts/installation-version.md)).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `identity.installation.view` | Installation | ✓ | — | — |

- **`identity.installation.view`**: `GET /api/v1/identity/installation`, the running release,
  its revision, the version the database records and an upgrade in progress.

### Registered by 018

Forms and submissions (018) add three, registered by the Forms module. Editors answer
enquiries, so every site role reads submissions; deleting them for good is kept to Designers
(018 clarification 1). See
[018 research FS-20](../../018-forms-submissions/research.md#fs-20-capabilities-and-audit).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `forms.submissions.view` | Site | ✓ | ✓ | ✓ |
| `forms.submissions.delete` | Site | ✓ | ✓ | — |
| `forms.email.test` | Installation | ✓ | — | — |

- **`forms.submissions.view`**: list, open, mark read or unread, search, filter and export
  submissions; see Spam and restore from it; see the spam counters, the unread count and the
  retention impact.
- **`forms.submissions.delete`**: delete submissions and spam.
- **`forms.email.test`**: send a test email to one's own address.
- **Building forms** needs only what editing a draft needs (`pages.page_drafts.edit`, or
  `pages.components.manage` for components), as for every block; a form reaches visitors with
  `pages.publishing.manage`.
- Whether email is configured (`GET /api/v1/forms/email-status`) needs only a signed-in user.

### Registered by 020

Site export and import (020) add two, registered by the Sites module. Both are held by
Administrators only: an archive holds every draft and setting of a site, and an import can
create or replace a site. See
[020 contracts/transfer-api.yaml](../../020-site-export-import/contracts/transfer-api.yaml).

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `sites.transfer.export` | Site | ✓ | — | — |
| `sites.transfer.import` | Installation | ✓ | — | — |

- **`sites.transfer.export`**: start an export of a site and list its transfers
  (`/api/v1/sites/{siteId}/…`).
- **`sites.transfer.import`**: upload, check, confirm, follow, cancel, download and delete
  transfers by id (`/api/v1/transfers/…`). These routes have no site to scope by, so they use
  the installation capability.
- While an import applies to a site, every content write on it is refused with
  `423 site.import_in_progress`, whatever the caller's capabilities (020 research ST-07).

## 3. What a caller sees

| Situation | Status | `errorCode` |
|---|---|---|
| Installation not set up, any `/api` call off the setup allow-list | 428 | `identity.setup_required` |
| No valid session | 401 | `identity.unauthenticated` |
| State-changing request without `X-QuickBuilder-Request: 1`, or with a foreign `Origin` | 403 | `identity.request_rejected` |
| Signed in, lacks an installation-scoped capability | 403 | `identity.forbidden` |
| Signed in, holds a role on the named site, lacks the capability there | 403 | `identity.forbidden` |
| Signed in, holds **no** role on the named site (and isn't an Administrator) | 404 | `sites.not_found`, byte-identical to a missing site apart from `traceId` |

All responses are `application/problem+json` in 001's shape (`type`, `title`, `status`,
`detail`, `errorCode`, `traceId`).

**404 versus 403**: a user who can't see a site mustn't learn that it exists. A user who
can see it is told honestly that they may not do this particular thing.

## 4. The anonymous allow-list

| Endpoint | Why |
|---|---|
| `GET /api/v1/identity/status` | The admin shell has to know whether to show setup or sign-in before anyone is signed in |
| `POST /api/v1/identity/setup` | Setup (accepted only while unclaimed, and only with the setup code) |
| `POST /api/v1/identity/sign-in` | Signing in |
| `POST /api/v1/identity/set-password-links/check` | Validating a link before asking for a password |
| `POST /api/v1/identity/set-password` | Using a link |
| `GET /health/live`, `GET /health/ready` | Orchestrator probes |
| `GET\|HEAD /_media/{file}` (public surface, 003 `ServePublicFile`) | Site visitors never authenticate; justified by 003 FR-026 |
| `GET\|HEAD /{path}` and any other method on it (public surface, 012 `RenderPage`) | Published pages for site visitors, and `405` for other methods; justified by 012 FR-022 |
| `GET\|HEAD /_assets/blocks.{hash}.css` (public surface, 012 `ServeBlockStylesheet`) | The published pages' stylesheet; justified by 012 research PR-10 |
| `GET\|HEAD /sitemap.xml`, `/sitemap-{n}.xml`, `/robots.txt` (public surface, 017 `Sitemap`, `Robots`) | Crawlers never authenticate; justified by 017 FR-023, FR-025 |
| `POST /_forms/{formId}`, and any other method on `/_forms/**` (public surface, 018 `Submit`) | Form submissions from site visitors, who never authenticate, and the `405` for other methods; justified by 018 FR-015 |
| `GET\|HEAD /_preview/l/{secret}` and `/_preview/l/{secret}/media/{file}`, and any other method on them (022 `LinkPreview`) | Preview links open without an account; the secret is checked on every request (022 research PV-07, PV-08) |
| `GET\|HEAD /_preview/sites/{siteId}/pages/{pageId}`, `/items/{itemId}` and `/media/{file}`, and any other method on them (022 `MemberPreview`) | Member previews; access is checked in the action, so every refusal is the same not-found page (022 FR-004, research PV-09) |
| `GET\|HEAD /_preview/assets/{file}`, `POST /_preview/form`, `GET\|HEAD /_preview/{**rest}` (022 `PreviewAssets`, `PreviewForm`, `PreviewFallback`) | The previews' stylesheet, the page a previewed form posts to, and the one answer to any other preview address; none reads content (022 research PV-10, PV-12) |
| `/admin/**` static assets and the SPA fallback | The shell has to load in order to show the sign-in screen |

Adding to this list means changing this document, the list in the architecture test,
and a spec that justifies it.

## 5. Declarations on the existing Sites endpoints

| Endpoint | Declaration | Handler change |
|---|---|---|
| `GET /api/v1/sites` | `[RequireSignedIn]` | Filters through `IAccessEvaluator.GetVisibleSitesAsync()`: all sites for Administrators, only granted sites otherwise (FR-022) |
| `POST /api/v1/sites` | `[RequireCapability("sites.create")]` | None |
| `GET /api/v1/sites/{siteId}` | `[RequireCapability("sites.view")]` | None |
| `POST /api/v1/sites/{siteId}/host-names` | `[RequireCapability("sites.host_names.manage")]` | None |
| `DELETE /api/v1/sites/{siteId}/host-names/{hostName}` (009) | `[RequireCapability("sites.host_names.manage")]` | None |
| `PUT /api/v1/sites/{siteId}/settings` (009) | `[RequireCapability("sites.settings.manage")]` | None |
| `POST /api/v1/sites/{siteId}/disable`, `…/enable` (009) | `[RequireCapability("sites.availability.manage")]` | None |
| `GET /api/v1/sites/languages` (009) | `[RequireSignedIn]` | None |
| `GET /` (public) | `[AllowAnonymous]` | None |

## 6. Rules for later modules

1. Register your capabilities in `Add<Module>Module()`. Never edit Identity to add them.
2. Name the site in the route as `{siteId}` for anything site-scoped. Never infer it
   from the `Host` header (001 FR-011b).
3. List endpoints that span sites use `[RequireSignedIn]` and filter through
   `IAccessEvaluator`. They never return rows from sites the caller can't see.
4. Don't copy access decisions into caches or tokens. Grants are read per request so
   that changes apply on the next request (FR-017).
5. Hiding a button in the admin shell is courtesy. The declaration is the enforcement
   (FR-024).

## Trash (021)

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `pages.trash.purge` | Site | ✓ | — | — |
| `cms.trash.purge` | Site | ✓ | — | — |
| `media.trash.purge` | Site | ✓ | — | — |

Permanent delete and emptying the trash need these. Viewing and restoring reuse the capabilities of
viewing and deleting each kind; the endpoints are in `specs/021-soft-delete-trash/contracts/trash-api.yaml`.
Restoring a file checks in the handler what deleting one checks: one's own upload or
`media.files.delete`, and the publishing capability when a live page shows it.

## Preview links (022)

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `editor.preview_links.manage` | Site | ✓ | — | — |

Listing and revoking every preview link of a site need it (022 FR-015). Creating, listing and
revoking one page's or item's links reuse that kind's edit capability, checked in the handler
(`pages.page_drafts.edit` or `cms.items.edit`); those endpoints declare `pages.content.view` and
are listed in `specs/022-draft-preview-links/contracts/preview-api.yaml`.
