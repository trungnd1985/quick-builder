# Feature Specification: Sites and Host Name Resolution

**Feature Branch**: `009-sites-hostname-resolution`

**Created**: 2026-09-26

**Status**: Draft

**Issue**: #9

**Input**: User description: "Sites and hostname resolution. Problem / goal: One installation hosts several websites, so the product needs a concept of a site and a way to decide which site a public request belongs to. An administrator can create sites, configure their hostnames, base URL and default language, and disable a site. In: site CRUD, hostname list per site with validation of duplicates, site resolution middleware from the Host header, forwarded-headers and base-path support behind a reverse proxy, site context available to all modules, EF Core global query filters keyed by SiteId, isolation tests. Out: TLS certificates and DNS (the operator's proxy handles these), site-level design tokens (#10)."

## Context

Feature 001 introduced the `Sites` module as its worked example. A site already exists as
a record with a name and a list of host names. An administrator can create a site, list
sites and add a host name. Public requests are resolved to a site from the requested host
name, and a request whose host name matches no site gets a not-found response that
reveals nothing. Forwarded headers are trusted only from proxies the operator names, the
installation can run under a configured base path, and site-owned records are restricted
to the current site automatically. Feature 002 placed these operations under
authorization. Feature 003 was the first module to store site-owned content (media).

So a site can be created, but not managed afterwards. It cannot be renamed. A host name
cannot be removed or corrected. There is no canonical address to build links from, no
default language, and no way to take a site offline or remove it. The isolation
guarantee is checked for host names and media, but not by a shared suite that every later
content module (pages, CMS, forms) must pass. This feature turns the site into a
complete, manageable concept that the upcoming content modules can build on.

## Clarifications

### Session 2026-09-26

- Q: What should visitors to a disabled site see? → **A: the same not-found response as
  an unknown host name.** A disabled site is indistinguishable from one that does not
  exist, so disabling reveals nothing about which sites the installation hosts.
- Q: Should deleting a site permanently remove all of its content, or wait for trash and
  restore (#21)? → **A: no deletion in this feature.** Disabling is the only way to take a
  site offline. Deleting sites, and what happens to their content, is left to the soft
  delete, trash and restore feature (#21).
- Q: Should the admin area also be reachable on each site's own address, or only on
  addresses the operator sets aside for administration? → **A: operator-configurable.** The
  operator may list admin host names. When the list is set, the administrative surface
  answers only on those host names and site host names serve only the public surface.
  When the list is empty, the administrative surface answers on every host name, as today.
- Q: Which languages can a site choose as its default language, and what does a new site
  get? → **A: a curated built-in list, English by default.** About 40 common languages,
  identified by standard tags such as `en`, `de`, `vi` and `pt-BR`. New and upgraded sites
  default to English (`en`). There is no installation-level language setting.
- Q: Should a site's address be allowed to use plain HTTP, or only HTTPS? → **A: HTTPS by
  default, HTTP allowed with a warning.** An administrator may choose HTTP for a site, and
  the admin shell then shows a visible warning on that site.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An administrator creates and edits a site's settings (Priority: P1)

An administrator creates a site for a new client. They give it a name, one or more host
names, a default language, and choose which host name is its primary address. They later
rename the site, change its default language and switch its primary address to a new
domain. Each change shows up in the site's details straight away.

**Why this priority**: Every content module that follows (pages, CMS, rendering, SEO)
needs a site with a name, a canonical address and a language. Without editable settings,
any mistake made at creation is permanent.

**Independent Test**: As an administrator, create a site with two host names and a
default language, and confirm that its details show the name, host names, primary
address and language. Rename it, change its language and make the other host name
primary, then confirm each change. Try invalid values (an empty name, an unknown
language) and confirm each is refused with a reason and changes nothing.

**Acceptance Scenarios**:

1. **Given** an administrator,
   **When** they create a site with a name, at least one valid host name and a default
   language,
   **Then** the site is created and enabled, the first host name is its primary host name,
   and its details show every setting.
2. **Given** an existing site,
   **When** an administrator changes its name, default language or primary host name,
   **Then** the change is saved, shown in the site's details and in the site list, and
   is used by the next public request to that site.
3. **Given** an existing site,
   **When** an administrator submits an empty or over-long name, a language not on the
   supported list, or a primary host name that is not one of the site's host names,
   **Then** the change is refused with a reason for each problem and nothing is saved.
4. **Given** a Designer or Editor on a site,
   **When** they try to change that site's settings,
   **Then** the change is refused as forbidden, and the admin shell does not offer it.
5. **Given** two administrators editing the same site at once,
   **When** the second saves changes based on settings the first has since changed,
   **Then** the second save is refused as out of date and nothing is overwritten
   silently.

---

### User Story 2 - An administrator manages a site's host names (Priority: P1)

An administrator adds `www.client.example` and `client.example` to a site, removes a
staging host name that is no longer used, and fixes a typo by removing the wrong name and
adding the right one. When they try to add a host name that another site already uses,
the product refuses and says that the name is taken, without saying which site has it.

**Why this priority**: Host names decide which site every public request reaches. A
wrong or stale host name sends visitors to the wrong content or to nothing, and today it
cannot be corrected.

**Independent Test**: Add a host name to site A and confirm that public requests for it
reach site A. Remove it and confirm that the next public request for it gets the
unknown-site response. Try to add a host name that site B holds, in a different letter
case and with a trailing dot, and confirm that it is refused as taken.

**Acceptance Scenarios**:

1. **Given** a site,
   **When** an administrator adds a valid host name that no site holds,
   **Then** it is added, and the next public request for that host name resolves to the
   site on every instance.
2. **Given** a host name already held by any site, including the same site,
   **When** an administrator tries to add it again in any spelling that means the same
   name (letter case, a trailing dot, the Unicode or ASCII form of an internationalised
   name),
   **Then** it is refused as taken. When another site holds it, the refusal does not
   reveal which site.
3. **Given** a site with several host names,
   **When** an administrator removes one that is not the primary,
   **Then** it is removed, and the next public request for it gets the unknown-site
   response on every instance.
4. **Given** a site,
   **When** an administrator tries to remove its primary host name or its only host name,
   **Then** it is refused with a reason saying that another host name must be made
   primary first, or that a site keeps at least one host name.
5. **Given** a host name that includes a port, a path, a scheme, a wildcard, spaces or
   characters not allowed in host names,
   **When** an administrator tries to add it,
   **Then** it is refused as invalid with a reason.
6. **Given** a host name removed from site A,
   **When** an administrator adds it to site B,
   **Then** it is accepted, and public requests for it reach site B.

---

### User Story 3 - Visitors reach the right site, directly or through a reverse proxy (Priority: P1)

A visitor opens `https://client.example/`. The operator's reverse proxy handles TLS and
forwards the request to the installation, which may run under a base path such as `/cms`.
The installation decides from the host name the visitor used that the request belongs to
the client's site, and every module handling the request sees that site and only its
data. A request for a host name that no site holds, or one that tries to spoof the host
through untrusted forwarded headers, never reaches a site it should not.

**Why this priority**: This is the core promise of multi-site hosting. Existing behaviour
from feature 001 must keep working as sites become editable, and it must hold for every
deployment shape the documentation supports.

**Independent Test**: With two sites and an installation behind a trusted proxy with a
base path, send public requests for each site's host names through the proxy and confirm
that each reaches its own site. Send the same requests with forwarded headers from an
address that is not trusted, and confirm that those headers are ignored. Send a request
for an unknown host name and confirm that the response is the standard unknown-site
response.

**Acceptance Scenarios**:

1. **Given** sites A and B with distinct host names,
   **When** a public request arrives for one of site A's host names, in any letter case,
   with or without a trailing dot and with any port,
   **Then** it is resolved to site A, and every module handling it sees only site A's
   data.
2. **Given** an installation behind a reverse proxy that the operator configured as
   trusted,
   **When** the proxy forwards a request with the visitor's host name, scheme and address,
   **Then** the site is resolved from the forwarded host name, and the scheme and visitor
   address are the forwarded ones.
3. **Given** forwarded headers from an address the operator has not configured as
   trusted,
   **When** the request is processed,
   **Then** the forwarded values are ignored, and the site is resolved from the host name
   of the direct connection.
4. **Given** an installation configured with a base path,
   **When** public requests arrive under that base path,
   **Then** they are resolved and served normally, and every address the product
   generates for a site includes the base path.
5. **Given** a public request whose host name matches no site,
   **When** it is processed,
   **Then** the response is the unknown-site response from feature 001, identical
   whatever sites exist, and it never falls back to another site.
6. **Given** a request on the administrative surface,
   **When** it is processed,
   **Then** no site is resolved from the host name, and any site it concerns is named
   explicitly in the request, as feature 001 requires.
7. **Given** an operator who has configured admin host names,
   **When** a request for an administrative path arrives on a site's host name,
   **Then** it is handled as a public request for that site and never reaches the
   administrative surface. The same request on an admin host name reaches the
   administrative surface normally.

---

### User Story 4 - An administrator disables and re-enables a site (Priority: P2)

A client's contract ends, so an administrator disables their site. From that moment the
site's host names no longer serve anything to visitors, but the site, its settings, its
content and its host names are kept. Site members can still work on it in the admin
shell, where it is clearly marked as disabled. When the client renews, the administrator
re-enables it and the site is back exactly as it was.

**Why this priority**: Taking a site offline without losing it is a routine operation
for anyone hosting several sites. It depends on the site settings from stories 1 and 2.

**Independent Test**: Disable a site that has media and confirm that public requests to
its host names, including its media addresses, get the unknown-site response on the next
request, byte-for-byte the same as for a host name no site holds. Confirm that the site is still listed and marked as disabled, and that its
host names cannot be added to another site. Re-enable it and confirm that public requests
work again and that nothing was lost.

**Acceptance Scenarios**:

1. **Given** an enabled site,
   **When** an administrator disables it after confirming,
   **Then** from the next public request on every instance, requests for its host names,
   including its media addresses, get the unknown-site response, identical to a host
   name that no site holds.
2. **Given** a disabled site,
   **When** site members open the admin shell,
   **Then** the site is listed and clearly marked as disabled, and they can keep working
   on it according to their roles.
3. **Given** a disabled site,
   **When** an administrator tries to add one of its host names to another site,
   **Then** it is refused as taken, because a disabled site keeps its host names.
4. **Given** a disabled site,
   **When** an administrator re-enables it,
   **Then** its host names serve it again from the next public request on every
   instance, with all settings and content unchanged.
5. **Given** a Designer or Editor,
   **When** they try to disable or re-enable a site,
   **Then** it is refused as forbidden.

---

### User Story 5 - Every module is proven to keep sites apart (Priority: P2)

A developer adding a new content module, such as pages, stores site-owned records. A
shared isolation suite, which every module that stores site-owned data must pass, proves
that one site's data can never be read, listed, changed or deleted through another site,
on either the public or the administrative surface. A module that forgets to restrict a
record by site fails the build.

**Why this priority**: The constitution makes site isolation non-negotiable, and the
modules that store the most data (pages, CMS, forms) are about to be built. A shared
suite catches mistakes before they ship, rather than relying on each module to
reinvent its own checks.

**Independent Test**: Run the isolation suite against the Sites and Media modules with
two populated sites and confirm that it passes. Add, in a test-only module, a site-owned
record without automatic restriction, and confirm that the build fails and names the
record.

**Acceptance Scenarios**:

1. **Given** two sites with data in every module that stores site-owned records,
   **When** the isolation suite runs,
   **Then** no public request resolved to one site returns, counts or reveals the other
   site's records.
2. **Given** an administrative operation that names site A,
   **When** it is given the identifier of a record that belongs to site B,
   **Then** it responds as though the record did not exist, and nothing in site B
   changes.
3. **Given** a site-owned record type without automatic site restriction, or a
   deliberate cross-site read without a stated reason and a dedicated test,
   **When** the build runs,
   **Then** it fails and names the offending record or read.
4. **Given** a new module that stores site-owned records,
   **When** it is added,
   **Then** the isolation suite covers it through a documented, small registration step,
   and the build fails if the module is not registered.

---

### Edge Cases

- **Host name spellings**: letter case, a trailing dot, surrounding spaces and the Unicode
  and ASCII forms of an internationalised name are treated as the same name, both when
  checking for duplicates and when resolving requests. Names are stored and shown in one
  normalised form.
- **Port in the request**: resolution ignores the port a request arrived on, as feature
  001 decided. Host names in site settings never include a port.
- **IP addresses and `localhost`**: accepted as host names so that operators can test
  without DNS, and subject to the same uniqueness rule.
- **Wildcard host names** (`*.example.com`): not supported; refused as invalid.
- **Two administrators adding the same host name to different sites at once**: exactly
  one succeeds and the other is refused as taken.
- **A host name removed while requests are in flight**: requests already being handled
  may finish. From the next request on every instance, the name no longer resolves.
- **A site renamed**: its identifier, host names, grants and content are unaffected. Only
  the displayed name changes.
- **Primary host name changed**: addresses the product generates from then on use the
  new primary host name. Redirecting visitors from other host names to the primary one
  is not part of this feature (it belongs with SEO and redirects, #17).
- **Default language changed**: no content is translated or moved. Later modules decide
  what the default language means for their content.
- **Several instances behind a load balancer**: a change to host names or to a site's
  enabled state must reach every instance before the operation reports success, or
  within a documented short bound when a shared cache is configured. Until a shared
  cache provider exists, the documentation keeps saying that multi-instance is not
  advertised, consistent with feature 001.
- **Installation with zero sites**: the admin surface still works, and public requests
  get the unknown-site response (feature 001, FR-014a).
- **Disabling the site an administrator is currently viewing**: the admin shell keeps the
  site open, marks it as disabled and explains that visitors no longer reach it.
- **Upgrade from feature 003**: every existing site becomes enabled. Its first host
  name, in creation order, becomes its primary host name, its address scheme becomes
  HTTPS, and its default language is set to English (`en`). Nothing that was
  public stops being public.
- **Forwarded headers with several proxy hops**: only hops from trusted addresses are
  honoured. A forwarded host name from an untrusted hop is never used for resolution.
- **Admin host names not configured**: the administrative surface stays reachable on
  every host name, including every site's, as in features 001 to 003. The operations
  documentation recommends setting admin host names for any installation that hosts
  client sites.
- **A misconfigured base path or trusted-proxy list**: start-up fails with a message that
  names the setting, as feature 001 requires.

## Requirements *(mandatory)*

### Functional Requirements

**Site settings**

- **FR-001**: Each site MUST have a name, one or more host names, exactly one primary host
  name chosen from those, an address scheme, a default language and an enabled or
  disabled state. The scheme MUST be HTTPS by default. An administrator MAY choose HTTP for
  a site, and the admin shell MUST then show a visible warning on that site's settings
  and in the site list that its links are not secure.
- **FR-002**: The site's base URL MUST be derived from its scheme, its primary host name
  and the installation's configured base path. It MUST NOT be entered as free text, so it
  can never disagree with the site's host names or the installation's base path. The
  base URL MUST be what every module uses when it needs a site's absolute address.
- **FR-003**: The default language MUST be chosen from a curated built-in list of about 40
  common languages, identified by standard language tags (for example `en`, `de`, `vi`,
  `pt-BR`). The admin shell MUST show each by its readable name. A new site's default
  language MUST be English (`en`) unless the administrator chooses another. Operators
  MUST NOT be able to change the list in this feature.
- **FR-004**: Administrators MUST be able to create a site with a name, at least one host
  name, an optional choice of primary host name (the first one by default), a scheme and
  a default language.
- **FR-005**: Administrators MUST be able to change a site's name, scheme, default
  language and primary host name. A change that is based on out-of-date settings MUST be
  refused rather than overwrite a newer change.
- **FR-006**: Every refusal MUST state a machine-readable reason (for example invalid
  name, invalid host name, host name taken, unsupported language, primary host name
  required, last host name, out of date), and the admin shell MUST show
  each as a clear message.

**Host names**

- **FR-007**: Administrators MUST be able to add host names to and remove host names from
  a site.
- **FR-008**: A host name MUST be unique across the whole installation, including
  disabled sites. Two spellings that mean the same name (FR-009) MUST count as the same
  name. When another site holds a name, the refusal MUST NOT reveal which site holds it.
- **FR-009**: Host names MUST be normalised before they are stored, compared or resolved:
  surrounding spaces and a trailing dot removed, letters lowercased, and internationalised
  names converted to their standard ASCII form. The admin shell MUST display
  internationalised names in their readable form.
- **FR-010**: A host name MUST be refused if it contains a scheme, port, path, wildcard,
  whitespace or any character not allowed in host names, or if it exceeds host-name
  length limits.
- **FR-011**: Removing the primary host name MUST be refused until another host name has
  been made primary. Removing a site's only host name MUST be refused.
- **FR-012**: Adding or removing a host name, or disabling or enabling a site, MUST
  take effect for public requests on every instance before the operation reports
  success. Where a shared cache makes this impossible to guarantee synchronously, it MUST
  take effect within a documented bound of no more than 5 seconds.

**Resolution and site context**

- **FR-013**: Every public request MUST be resolved to at most one enabled site from the
  host name the visitor used, as determined under FR-016. A request whose host name
  matches no site MUST get feature 001's unknown-site response and MUST NOT fall back to
  any site.
- **FR-014**: A public request whose host name belongs to a disabled site MUST NOT reach
  any module of that site, including media serving. It MUST get the same unknown-site
  response as a host name that no site holds, identical in status, headers and body, so
  that disabling a site reveals nothing about its existence.
- **FR-015**: Every module MUST be able to read the current request's site, meaning its
  identifier, name, base URL, default language and state, through the Sites module's
  public contract, without reading request headers itself. On the administrative
  surface, where a site is named in the request, modules MUST be able to read the same
  details for that site.
- **FR-016**: The host name, scheme and client address used for resolution and for
  generated addresses MUST come from forwarded headers only when the request arrives from
  a proxy the operator configured as trusted. By default, no proxy is trusted.
- **FR-017**: Public and administrative requests MUST work under the installation's
  configured base path, and every address the product generates MUST include it.
- **FR-018**: The administrative surface MUST continue to work without a resolved site
  and on an installation with zero sites. Its handling of site-named operations MUST
  follow feature 002's rules: not found for sites the user may not see, forbidden where
  the capability is missing.
- **FR-018a**: The operator MUST be able to configure a list of admin host names. When the
  list is not empty, requests for administrative paths on any other host name MUST be
  handled as public requests, so they never reach the administrative surface and never
  reveal that it exists. Health endpoints are exempt: they MUST remain reachable on every
  host name, so that container and load-balancer probes keep working. When the list is empty (the default), the administrative surface
  MUST answer on every host name. An admin host name MUST NOT also be a site host name:
  adding one to a site MUST be refused as taken, and a configured admin host name that a
  site already holds MUST fail start-up with a message naming the setting.

**Disable**

- **FR-019**: Administrators MUST be able to disable an enabled site and re-enable a
  disabled one after confirming. Disabling MUST keep the site's settings, host names,
  grants and content unchanged. Site members MUST still be able to work on a disabled
  site in the admin shell, where it MUST be clearly marked as disabled.
- **FR-020**: This feature MUST NOT offer deleting a site. Disabling is the only way to
  take a site offline. Site deletion, and what happens to its content, belongs to the
  soft delete, trash and restore feature (#21).
- **FR-021**: Other modules MUST learn that a site was disabled or re-enabled through
  integration events, so they can react without reading the Sites module's data.
  Their handlers MUST be idempotent.

**Authorization**

- **FR-022**: Creating, editing, disabling and enabling sites, and managing host names,
  MUST require capabilities that only Administrator holds in this feature. Every
  such operation MUST declare its capability, following feature 002.
- **FR-023**: Designers and Editors MUST be able to see the settings of sites on which
  they hold a role, including the base URL, default language and state, but MUST NOT be
  able to change them.

**Isolation**

- **FR-024**: Every site-owned record MUST be restricted to the current site
  automatically. A deliberate cross-site read MUST state its reason and have a dedicated
  test, and any other bypass MUST fail the build (feature 001, FR-013a).
- **FR-025**: The product MUST include a shared isolation suite that, for every module
  storing site-owned records, proves with at least two populated sites that no record of
  one site can be read, listed, counted, changed or deleted through the other site, on the
  public or administrative surface. The Sites and Media modules MUST pass it in this
  feature. A module that stores site-owned records and is not covered by the suite MUST
  fail the build.
- **FR-026**: The suite MUST also cover disabled sites: none of their records are
  reachable on the public surface.

**Admin shell, audit and operation**

- **FR-027**: The admin shell MUST provide screens to create a site, view and edit its
  settings, manage its host names, and disable and re-enable it. It MUST show usable
  empty, in-progress and error states, and MUST meet the translation-lookup and
  accessibility baselines from features 001 and 002.
- **FR-028**: Host name changes and disabling or re-enabling a site change
  what the public can reach, so they MUST be recorded in feature 002's security event
  record with the acting user, the site and the time.
- **FR-029**: The upgrade from the previous release MUST enable every existing site and
  set its primary host name, scheme and default language as described in Edge Cases,
  without making any public address stop working. An automated upgrade test from a
  snapshot of the previous release MUST prove it.
- **FR-030**: The operations documentation MUST describe running behind a reverse proxy
  (trusted proxies, forwarded headers, base path), how host names reach sites, how to
  restrict the administrative surface to admin host names, what visitors see for unknown
  and disabled sites, and that TLS certificates and DNS are the
  operator's responsibility.
- **FR-031**: The release smoke test MUST be extended to create a second site, confirm
  that each site's host name reaches its own site, disable one and confirm that it is no
  longer served.
- **FR-032**: Log output MUST refer to sites by identifier. It MUST NOT include requested
  host names for unresolved requests (feature 001).

### Key Entities

- **Site**: one website hosted by the installation. It has an identifier that never
  changes, a name, a scheme, a default language, an enabled or disabled state, its host
  names with one marked primary, and the times it was created and last changed. Every
  site-owned record in every module belongs to exactly one site.
- **Site Host Name**: one host name mapped to one site, stored in normalised form and
  unique across the installation. One per site is the primary host name.
- **Base URL**: the site's canonical absolute address, derived from its scheme, primary
  host name and the installation's base path. It is never stored as free text.
- **Supported Language**: an entry in the built-in list of languages a site may choose as
  its default, identified by a standard language tag.
- **Site Context**: the site that the current request concerns, and its details, as every
  module sees it. On the public surface it is resolved from the host name. On the
  administrative surface it is named by the request.
- **Site Lifecycle Event**: the notice other modules receive when a site is disabled or
  re-enabled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can create a site with two host names and a default
  language, and see a public request to it resolve, in under 2 minutes on a fresh
  reference installation.
- **SC-002**: Across the automated test suite, zero public requests reach a site other
  than the one that holds the requested host name, and zero reach a disabled site.
- **SC-003**: The isolation suite runs against every module that stores site-owned
  records, and finds zero records of one site reachable through another, on either
  surface.
- **SC-004**: 100% of duplicate host names, including every equivalent spelling in
  FR-009, are refused, and 100% of the invalid forms in FR-010 are refused, across a test
  set of at least 40 host names.
- **SC-005**: After a host name is removed, or a site is disabled or re-enabled, the
  very next public request on every instance reflects the change (or within
  the documented bound when a shared cache is configured).
- **SC-006**: Across the automated test suite, forwarded headers from untrusted
  addresses never influence which site a request resolves to.
- **SC-007**: On the reference environment (two cores, four gigabytes of memory), with
  100 sites and 500 host names configured, site resolution adds no more than
  5 milliseconds to the 95th-percentile public response time.
- **SC-008**: After upgrading from the previous release, 100% of existing sites are
  enabled, and 100% of previously working public addresses still work.
- **SC-009**: The site management journeys (create, edit, manage host names, disable,
  re-enable) can be completed with the keyboard alone, and 100% of their visible
  text resolves through the translation lookup.

## Scope

### In Scope

- Creating, viewing, editing, disabling and re-enabling sites.
- Managing a site's host names, with normalisation and installation-wide duplicate
  checks, and choosing its primary host name.
- The derived base URL and the default language.
- Resolving public requests to enabled sites from the host name, and the unknown-site
  response for unknown and disabled sites.
- Verified behaviour behind a reverse proxy: trusted forwarded headers and the configured
  base path.
- The site context, with site details, available to every module.
- Automatic site restriction of site-owned records, and the shared isolation suite.
- Site lifecycle integration events, security event records, admin shell screens,
  upgrade migration, documentation and the smoke-test extension.

### Out of Scope

- TLS certificates and DNS. The operator's reverse proxy and DNS provider handle them.
- Site-level design tokens and themes (#10).
- Redirecting non-primary host names to the primary one, and other redirects (#17).
- Multi-language content, translations and per-language addresses. This feature records
  only a default language.
- Wildcard host names, and resolving sites by path (for example one site per
  sub-path on a shared host).
- Deleting sites, and trash and restore for sites (#21). Disabling is the only way to
  take a site offline in this feature.
- Site export and import (#20).
- A per-site Administrator role. Administrator stays installation-wide (feature 002).
- A shared cache provider for multi-instance deployments.

## Assumptions

- **Only Administrators manage sites**: consistent with feature 002, where creating sites
  and assigning host names require Administrator. Designers and Editors can see the
  settings of their sites.
- **The base URL is derived rather than typed**: an administrator chooses a scheme and a
  primary host name, and the installation's base path completes it. This avoids a base URL
  that points at a host name the site does not hold.
- **The supported language list** is the curated built-in list in FR-003. It is not
  editable by operators in this feature. The admin shell's own language is unaffected.
- **Resolution is by host name only**, matching the constitution and feature 001. One
  installation-wide base path applies to all sites.
- **Most of the resolution and proxy machinery exists** from feature 001. This feature
  extends it and verifies it end to end, rather than replacing it.
- **Scale target**: up to 100 sites and 500 host names per installation, with no
  noticeable effect on resolution (SC-007). Larger installations still work but have no
  target in this feature.

## Dependencies

- Feature 001 (foundation skeleton): the Sites module, resolution from the host name, the
  split between the administrative and public surfaces, forwarded headers and base path,
  automatic site restriction and its bypass rule, the outbox and the smoke test.
- Feature 002 (identity and setup): capabilities, per-site grants, the forbidden versus
  not-found rule and the security event record.
- Feature 003 (media storage): the first module whose site-owned data the isolation suite
  and the disable behaviour must cover.
- The project constitution, in particular Principles III (upgrades never lose data), V
  (multi-site within one installation), IX (secure by default) and X (tests for
  critical paths, including site resolution).
- GitHub issue #9, which this feature implements and which the pull request closes.
