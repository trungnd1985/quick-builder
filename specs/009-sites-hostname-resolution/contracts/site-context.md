# Contract: Site Context, Resolution and Isolation

**Feature**: 009 (issue #9) | **Audience**: authors of modules and of the host

This is what every module may rely on about sites after this feature, and what every
module that stores site-owned data must do. It extends 001's site-context rules and
doesn't replace them.

## 1. Which surface a request is on

A request's surface is decided once, in `SiteResolutionMiddleware`, after forwarded
headers and the base path have been applied. Rows are checked in order, and the first
match wins:

| # | Path (after base path) | Host | Surface | Site |
|---|---|---|---|---|
| 1 | `/health/**` | any | operational | none |
| 2 | `/api/**`, `/admin/**` | in `Hosting:AdminHostNames`, or that list is empty | administrative | none resolved; named in the route where relevant |
| 3 | `/api/**`, `/admin/**` | anything else (list non-empty) | public | resolved as in row 4, then answered with the site's **public not-found**. The matched endpoint never runs. |
| 4 | anything else | any | public | the **enabled** site holding the normalised host, or `sites.not_resolved` |

- **Unknown and disabled hosts**: a disabled site's host names are indistinguishable from
  unknown ones in rows 3 and 4: same status, headers and body.
- **Public not-found**: one response for "this site has no such path". Unmatched public
  paths and row 3 both write it through the same `PublicNotFound` writer.

## 2. Reading the current site

| Need | Use | Available on |
|---|---|---|
| The site id, for query filters | `ISiteContext.SiteId` (`Shared.Infrastructure`) | public surface, and scoped background work that sets it |
| The site's details: name, base URL, language, status… | `ICurrentSite.Site` (`Sites.Contracts`) | public surface. Null on the administrative surface. |
| A named site's details on the administrative surface | `ISiteDirectory.FindByIdAsync(siteId)` | anywhere. Returns the site in any status. |

Rules:
- **Headers**: handlers never read `Host`, `X-Forwarded-*` or the base path themselves
  (001 FR-011a).
- **Base URL**: build absolute addresses from `SiteDto.BaseUrl`. Never assemble one from
  the request. It already includes `Hosting:BasePath` and uses the ASCII primary host
  name.
- **Memoisation**: within one request, `ICurrentSite.Site` and `FindByIdAsync` for the
  resolved site return the same memoised `SiteDto` without another query. There is **no
  cross-request cache**: a change is visible on every instance at the next request
  (research S-01). A module that wants to cache site details across requests must key
  them by `SiteId` and must also handle `Sites.SiteDisabled`, `Sites.SiteEnabled` and
  settings changes. No module needs to in this feature.
- **`IsHostNameTakenAsync(hostName)`** (new, additive): true when any site holds the
  normalised name, whatever its status. The host uses it for the admin-host-name conflict
  check at startup (S-13).
- **`FindByHostNameAsync`**: returns only enabled sites. It exists for the host's
  resolution middleware. An architecture test fails if anything else calls it.

## 3. `SiteDto` (additive)

```csharp
public sealed record SiteDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> HostNames,    // ASCII, primary first
    DateTimeOffset CreatedAtUtc)
{
    public required string PrimaryHostName { get; init; }
    public required string Scheme { get; init; }          // "https" | "http"
    public required string BaseUrl { get; init; }
    public required string DefaultLanguage { get; init; } // a tag from research S-09
    public required string Status { get; init; }          // "enabled" | "disabled"
    public required uint Version { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
```

The four positional parameters are unchanged. New values are `init` properties, so the
constructor shape other code may use doesn't change. Only Sites constructs `SiteDto`.

## 4. Host names

`QuickBuilder.Shared.Kernel.MultiSite.HostNames` is the only definition (research S-04):

- `bool TryNormalise(string? input, out string normalised)` returns the ASCII form. It is
  used for storage, comparison, resolution and admin host names.
- `string ToDisplay(string normalised)` returns the Unicode form for display.

No module may compare host names in any other way.

## 5. Site lifecycle events

Published by Sites through its outbox, in the same transaction as the change. Handlers
must be idempotent (001 FR-015).

| Event | When | Payload |
|---|---|---|
| `Sites.SiteCreated` (existing) | Created | `SiteId`, `Name`, `HostNames` |
| `Sites.HostNameAdded` | A host name is added to an existing site | `SiteId`, `HostName`, `ActorUserId` |
| `Sites.HostNameRemoved` | A host name is removed | `SiteId`, `HostName`, `ActorUserId` |
| `Sites.SiteDisabled` | The status changes to disabled | `SiteId`, `ActorUserId` |
| `Sites.SiteEnabled` | The status changes to enabled | `SiteId`, `ActorUserId` |

A disabled site keeps its data, grants and host names. Modules don't need to do anything
on `SiteDisabled`, because the public surface is already closed to it. Site deletion
doesn't exist yet (#21).

## 6. Obligations of a module that stores site-owned data

1. **Mark the record**: every such entity implements `ISiteOwned` and declares
   `HasQueryFilter(e => e.SiteId == CurrentSiteId)` on its own `DbContext` (001, and
   `SiteQueryFilterGuidance`).
   - *Guard*: `SiteFilterCoverageTests` checks every module's model.
2. **Cross sites only through the helper**: use `AcrossAllSites(reason)`, with a written
   reason and a dedicated test. Nothing else may cross sites.
   - *Guard*: the banned-API analyser, plus the architecture test on reasons.
3. **Register an isolation probe**: add `ISiteIsolationProbe` in
   `tests/QuickBuilder.IntegrationTests/Isolation/Probes/`, following research S-15.
   - `SeedAsync(client, siteId)` creates one of each site-owned record through the
     module's HTTP API, and returns their ids and public paths.
   - `AdminRequests(siteId, ids)` lists every administrative request that takes a record
     id: read, list, change, delete.
   - `PublicRequests(ids)` lists every public request that takes a record id.
   - *Guard*: `IsolationCoverageTests` fails when a module with an `ISiteOwned` entity
     has no probe, and when an endpoint with `{siteId}` and a record-id route value isn't
     exercised by any probe.
4. **Pass the suite**: `SiteIsolationSuite` must pass for the module. Across sites A and
   B, no read, list, count, change or delete on one reaches the other. Cross-site record
   ids answer `404`. A disabled site's records aren't reachable publicly.

A worked example lives in `Probes/MediaIsolationProbe.cs`.
`docs/contributing.md` → "Adding a module with site-owned data" points here.
