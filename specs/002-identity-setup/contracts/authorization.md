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
| `GET /` (public surface, 001 `ResolveCurrentSite`) | Site visitors never authenticate |
| `GET\|HEAD /_media/{file}` (public surface, 003 `ServePublicFile`) | Site visitors never authenticate; justified by 003 FR-026 |
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
