# Quickstart & Validation Guide

**Feature**: `009-sites-hostname-resolution` | **Date**: 2026-09-26

This guide explains how to check that site management and resolution work end to end,
and how to verify each success criterion. It covers validation only:
- the schema is in [data-model.md](./data-model.md);
- endpoints are in [contracts/sites-api.yaml](./contracts/sites-api.yaml);
- module obligations are in [contracts/site-context.md](./contracts/site-context.md);
- decisions are numbered `S-xx` in [research.md](./research.md).

---

## Prerequisites

- **Tools**: a container runtime, plus the .NET 10 SDK and Node 22+ for developers.
- **Starting point**: an installation that has completed setup (002 quickstart §1), with
  an Administrator session cookie in `$COOKIE`.
- **Host names**: the `curl` examples pass the `Host` header explicitly, so no DNS or
  hosts-file changes are needed.
- **State-changing calls** need `-H "X-QuickBuilder-Request: 1"` (002 R-11). Examples use
  `$API=http://127.0.0.1:8080/api/v1/sites`.

---

## 1. Create a site and reach it (SC-001, US1 #1, US3 #1)

1. In the admin shell, open **Sites → New site**. Enter the name "Client", the host names
   `client.test` and `www.client.test`, and the language **Deutsch**. Leave the scheme at
   HTTPS and save.
2. The site's page shows:
   - primary `client.test`;
   - base URL `https://client.test/`;
   - language `de`;
   - status **Enabled**.

**Timing**: from **New site** to the first successful public request below should take
under 2 minutes.

```bash
curl -s -H "Host: CLIENT.test." http://127.0.0.1:8080/         # 200 {"siteId":…,"siteName":"Client"}
curl -s -H "Host: client.test:8443" http://127.0.0.1:8080/     # 200, the port is ignored
curl -s -H "Host: nobody.test" http://127.0.0.1:8080/          # 404 sites.not_resolved
```

**Automated**: `SiteResolutionTests`, and the smoke test's two-site step (FR-031).

---

## 2. Edit settings, including a stale edit (US1 #2, #3 and #5)

```bash
V=$(curl -s -b "$COOKIE" $API/$S | jq .version)
curl -s -b "$COOKIE" -H "X-QuickBuilder-Request: 1" -H "Content-Type: application/json" -X PUT \
  -d "{\"name\":\"Client GmbH\",\"scheme\":\"https\",\"defaultLanguage\":\"de\",\"primaryHostName\":\"www.client.test\",\"version\":$V}" \
  $API/$S/settings                                   # 200, baseUrl https://www.client.test/
# The same request again, with the old $V:            # 409 sites.out_of_date
# "defaultLanguage":"xx"                              # 400 sites.unsupported_language
# "primaryHostName":"other.test"                      # 400 sites.primary_host_name_invalid
```

**In the admin shell**: open the same site in two tabs, save in one, then save in the
other. The second tab says the settings changed and offers to reload.

Switching the scheme to HTTP shows a warning badge on the site's page and in the site
list (S-08).

**Automated**: `SiteSettingsTests`.

---

## 3. Manage host names (SC-004, US2)

```bash
curl -s -b "$COOKIE" -H "X-QuickBuilder-Request: 1" -H "Content-Type: application/json" \
  -d '{"hostName":"bücher.test"}' $API/$S/host-names       # 201, hostName xn--bcher-kva.test
curl -s -H "Host: xn--bcher-kva.test" http://127.0.0.1:8080/  # 200, resolves to Client
```

Create a second site with `other.test`. Then try to add each of these to it, and expect
**409 `sites.host_name_taken`** for every one, with no mention of Client:
- `CLIENT.TEST`
- `client.test.`
- ` client.test `
- `xn--bcher-kva.test`
- `bücher.test`

Each of these gives **400 `sites.invalid_host_name`**:
- `client.test:80`
- `https://x.test`
- `x.test/path`
- `*.x.test`
- `a b.test`
- a 254-character name

Deleting host names:
- Delete the primary: **409 `sites.primary_host_name_removal`**.
- Delete `bücher.test`: **204**. The next public request for it gets `sites.not_resolved`.

**Automated**: `HostNamesTests` (unit, at least 40 inputs) and `HostNameManagementTests`,
including two concurrent adds of one name to two sites, where exactly one succeeds.

---

## 4. Disable and re-enable (US4, SC-002)

Upload an image to Client's media library first (003 quickstart §1). Note its
`publicPath`.

```bash
curl -s -b "$COOKIE" -H "X-QuickBuilder-Request: 1" -X POST $API/$S/disable   # 200, status disabled
diff <(curl -s -H "Host: client.test" http://127.0.0.1:8080/) \
     <(curl -s -H "Host: nobody.test" http://127.0.0.1:8080/) && echo identical
curl -s -o /dev/null -w "%{http_code}\n" -H "Host: client.test" http://127.0.0.1:8080/<publicPath>   # 404
```

Checks:
- In the admin shell, Client is marked **Disabled**, and its media library still opens and
  accepts uploads.
- Adding `client.test` to another site gives **409 `sites.host_name_taken`**.
- Disabling again gives **200** with no second security event.

**Enable**: the public requests above succeed again, and nothing was lost.

**Security events**: after a few seconds, the account security events show
`site_host_name_added`, `site_host_name_removed`, `site_disabled` and `site_enabled` for
the actions above, each once.

**Automated**: `SiteAvailabilityTests`, `SiteSecurityEventTests`, and the smoke test.

---

## 5. Changes reach every instance at once (SC-005)

Start a second app container from the same image, on another host port, pointing at the
same database (copy the `app` service in a local override file, and change only its
published port). The reference compose publishes a fixed port, so `--scale` can't do
this.

Remove a host name through instance 1, then
request it on instance 2: it is `sites.not_resolved` on the very first request. Repeat
with disable and enable.

**Automated**: `ResolutionAcrossInstancesTests` uses two `QuickBuilderApp` instances over
one database.

---

## 6. Behind a reverse proxy, with a base path (SC-006, US3 #2–#4)

Set:
- `Hosting__BasePath=/cms`
- `Hosting__KnownProxies__0=<proxy IP>`

Then put any reverse proxy in front that sets `X-Forwarded-Host`, `X-Forwarded-Proto` and
`X-Forwarded-For`.

Checks:
- `https://client.test/cms/` through the proxy returns Client, and the site's `baseUrl`
  reads `https://client.test/cms`.
- Media addresses start with `/cms/_media/`.
- Sending `X-Forwarded-Host: other.test` **directly** to the app, bypassing the proxy,
  changes nothing: resolution uses the real `Host`.
- With two trusted hops (a CDN in front of the proxy, both configured), the outer
  forwarded host wins. With an untrusted outer hop, it is ignored.

**Automated**: `ProxyResolutionTests` and `BasePathTests`.

---

## 7. Keep the admin area off client domains (FR-018a)

Set `Hosting__AdminHostNames__0=admin.test` and restart. Then check:
- `http://admin.test/admin/` shows the admin shell, and the API works there.
- `curl -H "Host: client.test" http://127.0.0.1:8080/admin/` answers **404**, byte for
  byte the same as `…/no-such-page` on `client.test`. The same goes for `/api/v1/sites`.
- `curl -H "Host: client.test" http://127.0.0.1:8080/health/ready` returns **200**.
  Health checks answer everywhere.
- Adding `admin.test` as a host name of any site gives **409 `sites.host_name_taken`**.
- Setting `Hosting__AdminHostNames__0=client.test`, a name a site holds, stops startup
  with a message naming `Hosting:AdminHostNames`.

**Automated**: `AdminHostNameTests`.

---

## 8. Isolation holds for every module (SC-003, US5)

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter "FullyQualifiedName~Isolation"
```

- `SiteIsolationSuite` runs every registered probe, currently Sites and Media, across
  sites A and B, and with B disabled.
- `IsolationCoverageTests` passes.
- **Checking the guard fires**: temporarily add an `ISiteOwned` entity without a probe to
  the test-only `WidgetsModule`. `IsolationCoverageTests` then fails and names it. Revert
  the change.

---

## 9. Upgrade from 003 (SC-008, FR-029)

```bash
dotnet test tests/QuickBuilder.IntegrationTests --filter "FullyQualifiedName~UpgradeFrom003Tests"
```

**Manually**: run the 003 image and create two sites with several host names and some
media. Replace the image with this build. Then check that:
- every site is **Enabled**, uses HTTPS and `en`, and has its first host name as primary;
- every host name and media address still works.

---

## 10. Performance and accessibility (SC-007, SC-009)

```bash
dotnet test tests/QuickBuilder.SmokeTests --filter "FullyQualifiedName~SiteResolutionPerformanceTests"
cd src/admin && npm test -- siteDetailView sitesView
```

- **Performance**: with 100 sites and 500 host names, p95 of `GET /` on a site host
  exceeds p95 of `/health/live` by no more than 5 ms.
- **Accessibility**: the vitest and axe checks pass. Manually, complete create, edit,
  add and remove host names, and disable and enable with the keyboard alone.
