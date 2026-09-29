# Feature Specification: Publishing and Rendering

**Feature Branch**: `012-publishing-rendering`

**Created**: 2026-09-29

**Status**: Draft

**Issue**: #12

**Input**: User description: "Publishing and rendering. Problem / goal: Nothing is visible to visitors until pages can be published and served. A user can publish a page, see the published version at its URL, view the publish history and roll back to a previous version. Visitors get server-rendered HTML that is cached and refreshed when content changes. In: publish/unpublish as an atomic audited operation, page versions and rollback, scheduled publish via the outbox worker, server-side renderer from page JSON to HTML including shared components, per-block renderers, output caching, dependency tracking (page to components) and cache invalidation on publish, a 404 page, performance budget tests. Out: collection-driven pages (#16), SEO metadata and sitemap (#17), CDN provider integrations."

## Context

Features 001 to 011 give every site a page tree (010), shared components such as a header
and footer (010), a visual editor with ten content block types (011) and site-level
design tokens (011). All of it is draft: feature 010 and 011 both guarantee that no change
alters anything visitors see, because nothing visitors could see exists yet. A request to
a site's host name for any page address gets the not-found response.

This feature makes sites visible. A site member publishes a page, and visitors who open
its address get complete HTML built on the server from the published copy of the page,
its shared components and the site's design. Every publish produces a version that is
kept, shown in a history, and can be rolled back to. Publishing can also be scheduled.
The rendered HTML is cached, and each publish refreshes exactly the pages it affects.

Constitution Principles VII (draft and published are separate worlds) and VIII
(rendering is server-side, cached and correctly invalidated) set the rules for this
feature, including its performance budgets.

## Clarifications

### Session 2026-09-29

- Q: How do design token changes (colours, fonts, spacing, content widths from feature
  011) reach visitors? → **A: a separate "Publish design" action.** It is recorded in
  the history and changes every published page of the site at once. Saving tokens changes
  only the draft design (FR-010).
- Q: What happens when a published page's address changes (slug change, move of the page
  or an ancestor, change of home page)? → **A: it moves at once.** The page is served at
  its new address from the next request. The admin shell warns before the change that
  the page is live and how many published pages will change address. Redirects from old
  addresses are #17 (FR-011).
- Q: May members with the Editor role publish? → **A: no.** Only Designers and
  Administrators publish, unpublish, schedule and roll back pages, components and the
  design. Editors prepare drafts; there is no formal approval step (FR-009).
- Q: When a page has a pending scheduled publish and someone publishes it by hand before
  that time, what happens to the schedule? → **A: the manual action cancels it.** A manual
  publish, unpublish or rollback cancels the pending scheduled publish, the member is told
  before confirming, and the history records the cancellation (FR-019).
- Q: When an unpublished page or component is deleted, is its publish history kept? →
  **A: yes, the history but not the content.** History entries are kept with the last
  title and address and appear in a site-wide publish log that only Administrators can
  view. The content of old versions is deleted with the page (FR-006, FR-017).
- Q: Before publishing, can signed-in members preview the draft rendered exactly as
  visitors would see it? → **A: no, not in this feature.** The editor canvas, which uses
  the same block styling (SC-008), is the preview. Rendered draft previews belong with
  preview links in #22. The renderer serves only published content (FR-008).
- Q: Should the built-in not-found page show the site's published header and footer? →
  **A: no.** It is a standalone page in the site's published design (colours, fonts)
  with a message and a link to the home page, and no shared components (FR-030).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A site member publishes a page and visitors see it at its address (Priority: P1)

A designer has built the "About" page in the editor. They press Publish, and the admin
shell shows the page as published, with the time and their name and a link to its public
address. They open `https://example.com/about` in a private window and see the page as it
looked on the canvas, with the site's colours and fonts. Next they change the heading in
the editor. The public page is unchanged. When they publish again, the new heading appears
at the address.

**Why this priority**: This is the whole point of the feature. Until a page can be
published and served, no site built with the product has any visitors.

**Independent Test**: Build a page that uses all ten block types, publish it, and request
its address from the site's host name. Confirm that the response is complete HTML that
shows every block, that it matches the golden output for that page, and that it works
with scripts turned off. Change the draft and confirm the public page is unchanged. Publish
again and confirm the change appears.

**Acceptance Scenarios**:

1. **Given** a page with a valid draft,
   **When** a site member with the publish capability publishes it,
   **Then** a new published version is created from the draft in one step, the page is
   marked as published with the time and user, and its address serves the new version.
2. **Given** a published page,
   **When** a site member changes its draft,
   **Then** visitors keep seeing the published version, and the admin shell shows that
   the draft has unpublished changes.
3. **Given** a request to a site's host name for the address of a published page,
   **When** it is served,
   **Then** the response is a complete HTML document, built on the server, that shows the
   page's content without needing scripts in the visitor's browser.
4. **Given** a draft that does not pass validation against the page schema contract,
   **When** a site member tries to publish it,
   **Then** the publish is refused with the reason and location, and the published version
   and address stay as they were.
5. **Given** a request for an address that has no published page, or a page that exists
   only as a draft,
   **When** it is served,
   **Then** the visitor gets the site's not-found page with a not-found status, without
   revealing whether a draft exists at that address.
6. **Given** two site members publishing the same page at once,
   **When** both publish,
   **Then** each publish produces its own complete version, one after the other, and the
   address serves the later one. No version mixes content from both.

---

### User Story 2 - Shared components and design are published once and appear on every page (Priority: P1)

The site's header component holds the logo and navigation buttons. The designer publishes
the header once. Every published page that references it now shows it. Later they add a
"Blog" button to the header and publish only the header. Every published page shows the new
button at once, and no page needs republishing. The same holds for the site's design:
the designer changes the brand colour, checks it in the editor, and presses "Publish
design"; every published page takes the new colour together.

**Why this priority**: Almost every page uses a header and footer. If components had to
be copied into each page's version, a one-line change to the header would mean
republishing the whole site, and pages would drift apart.

**Independent Test**: Publish a header component and three pages that reference it. Change
the header draft and confirm that no page changes. Publish the header and confirm that all
three pages show the new header on their next request, and that a published page that does
not reference the header is not refreshed.

**Acceptance Scenarios**:

1. **Given** a component,
   **When** a site member with the publish capability publishes it,
   **Then** a new published version of the component is created, and every published page
   that references it shows the new version on its next request.
2. **Given** a page draft that references a component that has never been published,
   **When** a site member tries to publish the page,
   **Then** the publish is refused, naming the unpublished components.
3. **Given** a published component that a currently published page version references,
   **When** a site member tries to unpublish or delete it,
   **Then** it is refused, stating how many published pages use it.
4. **Given** a component publish,
   **When** it completes,
   **Then** only pages whose published version references that component are refreshed;
   no other page's cached output is discarded.
5. **Given** changes to the site's design tokens,
   **When** a site member with the publish capability publishes the design,
   **Then** every published page of the site uses the new design on its next request, and
   until then no token change is visible to visitors.

---

### User Story 3 - A site member views the publish history and rolls back (Priority: P1)

The client reports that yesterday's change to the "Pricing" page shows the wrong prices.
The designer opens the page's publish history. It lists every version with its number, time,
the person who published it and how it came about (publish, scheduled publish, rollback). They
choose yesterday morning's version and roll back. Visitors see the old prices at once. The
draft still holds the new prices so the designer can fix them and publish again.

**Why this priority**: Publishing without a way back makes every change risky. Rollback
is what makes it safe to publish often.

**Independent Test**: Publish a page three times with different content, roll back to
version 1, and confirm that the address serves version 1's content, that a new version 4
records the rollback, that the draft is unchanged, and that the history shows all four
versions with who, when and how.

**Acceptance Scenarios**:

1. **Given** a page or component with published versions,
   **When** a site member opens its history,
   **Then** they see every retained version, newest first, with its number, time, user and
   kind (publish, scheduled publish, rollback, unpublish), and which version is live.
2. **Given** an earlier version,
   **When** a site member with the publish capability rolls back to it,
   **Then** that version's content is published again as a new version that records it as a
   rollback of the earlier one, and the draft is not changed.
3. **Given** an earlier version,
   **When** a site member chooses to copy it into the draft,
   **Then** the draft is replaced with that version's content under the out-of-date rule
   from feature 010, and nothing visitors see changes until it is published.
4. **Given** a version that referenced a component that has since been deleted,
   **When** a site member tries to roll back to it,
   **Then** it is refused, naming the missing components.

---

### User Story 4 - A site member unpublishes a page (Priority: P2)

A seasonal offer page should no longer be public. The designer unpublishes it. Its address
now gives the not-found page, the page and its draft stay in the admin shell, and its
history records the unpublish so it can be published again later.

**Why this priority**: Taking content down is needed, but it happens less often than
publishing. It depends on story 1.

**Independent Test**: Unpublish a published page. Confirm that its address serves the
not-found page on the next request, even when the page was cached. Confirm that the draft
and history remain, and that publishing again makes the page public at its current
address.

**Acceptance Scenarios**:

1. **Given** a published page,
   **When** a site member with the publish capability unpublishes it,
   **Then** its address serves the not-found page from the next request, and the history
   records the unpublish with time and user.
2. **Given** an unpublished page with child pages that are published,
   **When** visitors request the child pages,
   **Then** the child pages are still served at their addresses.
3. **Given** a published page,
   **When** a site member tries to delete it,
   **Then** it is refused until the page has been unpublished.

---

### User Story 5 - A site member schedules a publish or unpublish (Priority: P2)

The marketing team wants the new product page to go live on Monday at 09:00 and the
launch banner page to go offline on Friday at 18:00. The designer reviews the page, schedules
its publish for Monday 09:00, and schedules the unpublish of the
banner page. The page tree shows both schedules. On Monday at 09:00 the product page is
live, and its history records a scheduled publish.

**Why this priority**: Scheduling is common for launches and campaigns but is not needed
to make a site visible. It builds on stories 1 and 4.

**Independent Test**: Schedule the publish of a page two minutes ahead, then change the
draft after scheduling. Confirm that at the scheduled time the version reviewed at
scheduling goes live, not the later draft. Schedule an unpublish and confirm it happens.
Stop the application across a scheduled time, start it again and confirm that the
scheduled action happens once, promptly after start.

**Acceptance Scenarios**:

1. **Given** a page with a valid draft,
   **When** a site member with the publish capability schedules its publish for a future
   time,
   **Then** the content of the draft at that moment is kept for the schedule, and at the
   scheduled time it is published as a new version recorded as a scheduled publish.
2. **Given** a scheduled publish,
   **When** the draft changes afterwards,
   **Then** the schedule still publishes the content reviewed at scheduling, and the admin
   shell shows that the draft differs from what is scheduled.
3. **Given** a published page,
   **When** a site member schedules its unpublish,
   **Then** at that time the page is unpublished, and the history records it as scheduled.
4. **Given** a schedule,
   **When** a site member cancels or changes its time before it runs,
   **Then** nothing happens at the old time.
5. **Given** a pending scheduled publish,
   **When** a site member publishes, unpublishes or rolls back the same page by hand,
   **Then** they are told first that the schedule will be cancelled, and after they
   confirm, the schedule is cancelled and the history records it, so older content never
   replaces the newer publish.
6. **Given** a scheduled time that passes while the application is not running, or
   while several instances are running,
   **When** the application is running again,
   **Then** the scheduled action is carried out exactly once, and its recorded time is the
   time it actually took effect.
7. **Given** a scheduled publish that can no longer be carried out (for example it
   references a component that has since been deleted),
   **When** its time comes,
   **Then** it is not carried out, the page stays as it was, and the admin shell shows the
   schedule as failed with the reason.

---

### User Story 6 - Visitors get fast pages that are always up to date (Priority: P2)

Visitors to a published site get pages quickly because rendered pages are reused. When the
designer publishes a change, visitors see it within seconds, on every instance of the
application, and never see a mix of old and new content.

**Why this priority**: Speed and freshness are what visitors notice. Correct caching also
protects the server when a page gets heavy traffic. It depends on stories 1 to 4 because
every publish action must refresh the right pages.

**Independent Test**: Warm the cache for a site of 50 pages, then publish one page, one
component used by 10 pages, and roll back one page. After each action, confirm that
exactly the affected pages serve new output within the freshness target and that every
other page is still served from cache. Repeat with two application instances and a shared
cache configured.

**Acceptance Scenarios**:

1. **Given** a published page requested before,
   **When** it is requested again and nothing it depends on has changed,
   **Then** it is served from the cache.
2. **Given** any publish, unpublish, rollback or scheduled action, a change to a published
   page's address, or a change of the home page,
   **When** it completes,
   **Then** every affected page serves the new content within the freshness target, and
   no unaffected page's cached output is discarded.
3. **Given** several application instances with a shared cache configured,
   **When** a publish happens on one instance,
   **Then** every instance serves the new content within the freshness target.
4. **Given** the reference environment,
   **When** the performance budget tests run,
   **Then** they fail the build if a page render or cached response exceeds its budget or
   a published page ships more JavaScript than allowed.

---

### User Story 7 - A visitor who follows a broken link gets a helpful not-found page (Priority: P3)

A visitor follows an old link to `/summer-sale`, which no longer exists. They get a page
that says it could not be found, in the site's language and style, with a link to the
site's home page. The browser and search engines get a not-found status.

**Why this priority**: Every site needs a clear not-found page, but the default response
is acceptable while the rest of the feature is built.

**Independent Test**: Request an address with no published page on a site that has a
published home page, and confirm the response has a not-found status and shows the
not-found page with a working link home, using the site's published design. Request an
unknown host name and confirm the response is still feature 009's.

**Acceptance Scenarios**:

1. **Given** a site's host name and an address with no published page,
   **When** a visitor requests it,
   **Then** they get the not-found page with a not-found status, showing a message in the
   site's default language and, if the home page is published, a link to it.
2. **Given** an unknown host name or a disabled site,
   **When** a visitor requests any address,
   **Then** the response is feature 009's not-found response, unchanged.

---

### Edge Cases

- **A page's address changes while it is published** (its slug changes, it or an ancestor
  moves, or the home page changes): the page is served at its new address from the next
  request and the old address gives the not-found page (FR-011). Redirects are #17.
- **Letter case and trailing slashes**: `/About` and `/about/` serve the page at `/about`,
  because addresses are unique ignoring letter case (feature 010). Canonical addresses and
  redirects are #17.
- **The home page is not published**: the site root `/` serves the not-found page.
- **Links to pages that are not published or were deleted**: they are rendered as plain
  text without a link, so visitors never follow a link into a not-found page from the
  site's own content.
- **Image whose media file was deleted**: the image is left out of the rendered page;
  nothing broken is shown to visitors.
- **Links and colours that refer to deleted design tokens** render with the block type's
  default, as in the editor (feature 011).
- **Embeds**: rendered as the provider's video player, loaded only when it comes into view.
  Opening a page that has no embed contacts no third party.
- **Publishing a page with no content** (an empty draft): allowed; it renders an empty page
  with the site's published header and footer only if the page references them.
- **Very large pages** (at the page schema contract limits of 5,000 nodes and 1 MiB):
  they can be published and render correctly; the performance budget applies to the
  representative page in Success Criteria, not to pages at the limits.
- **A document written in an older schema version**: the renderer renders every schema
  version still present in stored versions (constitution Principle VI). Only version 1
  exists today.
- **Two publishes of the same page at the same moment**: both succeed, one after the
  other, and version numbers never repeat or skip.
- **A publish while the cache is unavailable**: the publish still succeeds; visitors get
  freshly rendered pages until the cache is back, and never stale content that should
  have been refreshed.
- **A request arriving while a page is being published**: the visitor gets either the old
  version or the new one, complete, never a mix.
- **A site is disabled or re-enabled** (feature 009): disabled sites keep serving
  feature 009's not-found response; published content and history are kept, and are
  served again once the site is re-enabled.
- **A host name or base URL changes** (feature 009): pages are served at the new host
  name and base URL on the next request, including absolute addresses inside the page.
- **Many scheduled actions due at the same moment** (for example 200 pages scheduled for
  09:00): all are carried out within the scheduling precision target.
- **Scheduled time in the past**: refused with a reason; publish now instead.
- **Version retention**: see FR-013.
- **Upgrade from the previous release**: no page or component is published before this
  feature. The upgrade adds empty published storage and history, and every site keeps
  serving the not-found response until something is published.

## Requirements *(mandatory)*

### Functional Requirements

**Publishing pages and components**

- **FR-001**: Site members with the publish capability MUST be able to publish a page. A
  publish MUST validate the draft against the page schema contract, create a new published
  version holding a copy of the draft content and the page title, make that version the
  live one and record the action in the history, all in one step that either completes
  entirely or changes nothing.
- **FR-002**: Site members with the publish capability MUST be able to publish a
  shared component, under the same rules as a page. A component's published version MUST
  be used by every published page that references that component.
- **FR-003**: Publishing a page MUST be refused, naming the components, when its draft
  references a component that has no published version.
- **FR-004**: Unpublishing or deleting a component MUST be refused, stating how many
  published pages use it, while the live version of any page references it.
- **FR-005**: Site members with the publish capability MUST be able to unpublish a
  published page. From the next request its address MUST serve the not-found page. Its
  draft, versions and history MUST be kept, and publishing again MUST make it public at
  its current address.
- **FR-006**: Deleting a published page MUST be refused until it has been unpublished.
  Deleting an unpublished page or component MUST remove the content of its versions and
  its pending schedules, as feature 010 removes its draft, but MUST keep its history
  entries, labelled with its last title (or name) and address (FR-017).
- **FR-007**: Editing a draft, a page title, or a component draft MUST NOT change anything
  visitors see until it is published (constitution Principle VII). The admin shell MUST
  show, for each page and component, whether it is published, when and by whom, whether
  the draft has changes not yet published, and any pending schedule.
- **FR-008**: The public rendering path MUST read only published data and MUST NOT be able
  to change content (constitution Principle VII).
- **FR-009**: Publishing, unpublishing, scheduling, cancelling schedules and rolling back
  pages, components and the design MUST require a new named, site-scoped publish
  capability in feature 002's model. Administrator MUST hold it on every site and
  Designer on the sites where they have the role. Editor MUST NOT hold it; Editors can
  still view status and history (FR-016) and copy a version into a page draft (FR-015).

**Design**

- **FR-010**: Published pages MUST use the site's published design tokens (colours,
  typography, spacing, content widths). Saving the draft tokens (feature 011, FR-034)
  MUST NOT change what visitors see. Site members with the publish capability MUST be
  able to publish the design as a separate action that follows FR-001's all-or-nothing
  rule, is recorded in a design history under FR-012 and FR-013, can be rolled back under
  FR-014, and makes every published page of the site use the new design from its next
  request. Before a site's design is first published, pages MUST render with the default
  tokens from feature 011.

**Addresses of published pages**

- **FR-011**: A published page's address MUST follow the page tree (feature 010): after a
  slug change, a move of the page or an ancestor, or a change of home page, the page MUST
  be served at its new address from the next request and its old address MUST serve the
  not-found page, unless another published page now has it. Before such a change is
  saved, the admin shell MUST warn that it changes the address of published pages and
  state how many. Redirects from old addresses belong to #17.

**Versions, history and rollback**

- **FR-012**: Every publish, scheduled publish, rollback and unpublish MUST be recorded in
  the history of the page or component, with a version number that increases by one per
  published version and never repeats, the time, the user (or the schedule and the user
  who set it), and the kind of action.
- **FR-013**: Published versions MUST be kept so that rollback is possible. At least the
  50 most recent versions of each page and component MUST be kept, and the live version
  MUST never be removed. The history entry of every action MUST be kept for as long as
  the page or component exists, even if its version content has been removed.
- **FR-014**: Site members with the publish capability MUST be able to roll back to any
  retained version. A rollback MUST publish that version's content as a new version
  recording which version it restores, MUST follow FR-001's all-or-nothing rule and MUST
  NOT change the draft. A rollback to a version that references a component that no
  longer exists MUST be refused, naming the missing components.
- **FR-015**: Site members who may save the draft MUST be able to copy a retained version
  into the draft, under feature 010's out-of-date rule.
- **FR-016**: Site members who may view pages MUST be able to view the history of a page
  or component and the content of each retained version.
- **FR-017**: History entries MUST outlive the page or component they describe. Each site
  MUST have a publish log listing every history entry of the site (pages, components and
  design, including deleted pages and components), newest first, filterable by page or
  component and by user. Only Administrators MUST be able to view the publish log. The
  log shows who did what and when, never the content of deleted versions.

**Scheduled publish and unpublish**

- **FR-018**: Site members with the publish capability MUST be able to schedule the
  publish of a page or component, and the unpublish of a page, at a future time. Times are
  entered and shown in the member's own time zone, which the admin shell names next to
  the time, and are kept as absolute points in time. A page or component MUST have at most one pending publish and one
  pending unpublish schedule. Scheduled times in the past MUST be refused.
- **FR-019**: A scheduled publish MUST publish the draft content as it was when the
  schedule was set, validated then. Later draft changes MUST NOT alter what is scheduled.
  A manual publish, unpublish or rollback of the same page or component MUST cancel its
  pending scheduled publish. The admin shell MUST say so before the member confirms, and
  the history MUST record the cancellation.
- **FR-020**: Scheduled actions MUST be carried out by the built-in background jobs, within
  one minute of their time while the application runs, and exactly once, including when
  the application was stopped over the scheduled time and when several instances run.
  Members MUST be able to cancel or reschedule a pending schedule before it runs.
- **FR-021**: A scheduled action that can no longer be carried out MUST leave the page or
  component unchanged, be recorded as failed with a reason, and be shown as failed in the
  admin shell.

**Rendering**

- **FR-022**: Requests to a site's host name, for an address that belongs to a published
  page, MUST receive a complete HTML document rendered on the server from the page's live
  version, the live versions of the components it references and the site's published
  design. The content MUST be readable without scripts in the visitor's browser
  (constitution Principle VIII).
- **FR-023**: Each block type MUST have exactly one renderer, defined with the rest of its
  block definition (feature 011, FR-010), so adding a block type does not change any other
  renderer. Every block type MUST render the same layout the editor canvas shows at each
  preview width, using the same block styling.
- **FR-024**: The rendered page MUST carry the page's published title as the document
  title and the site's default language. Other metadata (description, social cards,
  canonical addresses, sitemap) belongs to #17.
- **FR-025**: Every value from content MUST be encoded for the place it is written to
  (text, attribute, style, address). Rich text MUST be produced from its structured form
  through an allow-list of the formats feature 011 permits. Links MUST only use the
  schemes feature 011 allows, and links that open in a new tab MUST NOT give the opened
  page access to the site's page (constitution Principle IX).
- **FR-026**: Links to pages of the same site MUST be written with the linked page's
  current address and the site's base URL. Links to pages that are not published or no
  longer exist MUST be rendered as plain text.
- **FR-027**: Images MUST be served from the site's media address space (feature 003);
  an image whose file no longer exists MUST be left out. Embeds MUST render only the
  allow-listed providers' players, and MUST NOT contact the provider until the embed comes
  into view.
- **FR-028**: A published page MUST ship no more than 100 KB of JavaScript unless a block
  on it explicitly requires more; none of the ten block types in feature 011 does.
- **FR-029**: The renderer MUST render every schema version present in stored published
  versions.

**Not found**

- **FR-030**: An address on a known, enabled site's host name that has no published page
  MUST receive a not-found status with a built-in not-found page that uses the site's
  published design, shows a message in the site's default language, and links to the home
  page when it is published. It MUST NOT include shared components; it stands alone with
  the published colours and fonts. The response MUST NOT reveal whether a draft or unpublished
  page exists at that address. Unknown host names and disabled sites keep feature 009's
  response.

**Caching and invalidation**

- **FR-031**: Rendered pages MUST be cached per site and reused until something they
  depend on changes. Cache entries MUST be namespaced by site (constitution Principle V).
- **FR-032**: The product MUST record, for each published page, which components it
  depends on, and MUST use that record to refresh exactly the affected pages when: the page
  is published, unpublished or rolled back; a component it references is published or
  rolled back; the site's design is published; its address changes; the home page
  changes; a page it links to is published, unpublished, moved or deleted; or the site's
  host names or base URL change.
- **FR-033**: Refreshes MUST reach every instance within 5 seconds when a shared cache is
  configured, and MUST NOT discard unaffected pages. A visitor MUST never be served a
  version older than the live one once the refresh has completed.
- **FR-034**: A page MUST be served correctly when the cache is unavailable, by rendering
  it fresh.
- **FR-035**: Responses MUST let browsers and intermediate caches check whether a page is
  still current, so an unchanged page is not sent again. Integration with specific CDN
  providers is out of scope.

**Authorization, isolation, events and operation**

- **FR-036**: Every publishing operation MUST declare its capability and answer as not
  found for sites the user may not see and as forbidden on a permitted site where the
  capability is missing, following feature 002.
- **FR-037**: Published versions, history entries, schedules and dependency records are
  site-owned. They MUST be restricted to their site automatically and pass the shared
  isolation suite from feature 009, which MUST also prove that no published content of one
  site can be served on another site's host name.
- **FR-038**: Other modules MUST learn through integration events when a page or component
  is published, unpublished or rolled back, and when a site's design is published, so that
  later features (sitemap and SEO in #17) can react. Event handlers MUST be idempotent.
- **FR-039**: The admin shell MUST offer publish, unpublish, schedule, cancel schedule,
  history, rollback, copy to draft and view live page for pages, and the equivalent for
  components, from the page tree, the component list and the editor, plus "Publish design"
  on the design screen and the site publish log for Administrators. It MUST confirm
  unpublish and rollback, show usable empty, in-progress and error states, and meet the
  translation-lookup and accessibility baselines of features 001 and 002.
- **FR-040**: Every refusal MUST state a machine-readable reason (for example invalid
  document, unpublished components, component in use by published pages, page is
  published, missing components in version, schedule in the past, schedule already
  pending, forbidden), and the admin shell MUST show each as a clear message.
- **FR-041**: Automated performance budget tests on the reference environment MUST fail
  the build when the budgets in SC-003 and SC-004 or FR-028 are exceeded.
- **FR-042**: The upgrade from the previous release MUST add the published storage,
  history, schedules and dependency records without changing drafts, proven by the
  automated upgrade test from a snapshot of the previous release.
- **FR-043**: The release smoke test MUST be extended to publish a page with a header
  component, request it from the site's host name, check the content, unpublish it and
  confirm the not-found response.
- **FR-044**: Log output MUST refer to pages, components and versions by identifier and
  MUST NOT include content, titles or visitor data beyond what request logs already
  record (constitution Principle IX).

### Key Entities

- **Published Version**: an immutable copy of a page's or component's content (and, for a
  page, its title) at the moment of a publish, with a version number, the schema version,
  the time, the user and the kind of action that produced it.
- **Live Version**: the published version that visitors are currently served for a page or
  component; none for an unpublished page.
- **Publish History Entry**: the audit record of one publish, scheduled publish, rollback,
  unpublish, cancelled or failed schedule: who, when, what, and which version. It is kept
  after the page or component is deleted, with its last title (or name) and address.
- **Publish Log**: the site-wide list of all publish history entries, visible to
  Administrators.
- **Publish Schedule**: a pending publish (with the content captured at scheduling) or
  unpublish of a page or component at a future time, with its state (pending, done,
  cancelled, failed).
- **Published Design**: the site's design tokens as last published; used by every
  published page of the site.
- **Rendered Page**: the cached HTML output of a published page for a site, with the set of
  things it depends on.
- **Page Dependency**: the recorded link from a published page to the components, design
  and linked pages its rendered output depends on.
- **Publish Capability**: a new named, site-scoped permission to publish, unpublish,
  schedule and roll back pages, components and the design; held by Administrators and
  Designers.
- **Design History Entry**: the audit record of one publish or rollback of the site's
  design.
- **Not-Found Page**: the built-in page served for addresses with no published page on a
  known, enabled site.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A site member can publish a page they have built and open it at its public
  address in under 1 minute, and roll back to an earlier version in under 30 seconds from
  opening the history.
- **SC-002**: After a publish, unpublish, rollback or component publish, visitors on every
  instance see the new content within 5 seconds in 95% of cases, and within 30 seconds in
  all cases, on the reference environment.
- **SC-003**: On the reference environment (two cores, four gigabytes of memory), a
  representative page of 200 blocks with a header and a footer component is served in
  under 200 milliseconds at the 95th percentile when it is not cached, and in under 20
  milliseconds when it is.
- **SC-004**: No published page built from feature 011's block types ships more than
  100 KB of JavaScript.
- **SC-005**: Across the automated test suite, zero visitor responses contain draft
  content, content from another site, or a mix of two versions, and zero cached pages are
  served after the refresh for a change that affects them has completed.
- **SC-006**: Publishing one component used by 10 pages of a 50-page site refreshes exactly
  those 10 pages; the other 40 remain cached.
- **SC-007**: 100% of scheduled actions in the test suite are carried out exactly once and
  within one minute of their time, including across an application restart and with two
  instances running.
- **SC-008**: Every block type has golden-file tests of its rendered output, and the
  rendered output of a page matches the editor canvas layout at desktop, tablet and mobile
  widths.
- **SC-009**: The publishing journeys can be completed with the keyboard alone, and 100%
  of their visible text, and of the not-found page, resolves through the translation
  lookup.

## Scope

### In Scope

- Publish and unpublish of pages, and publish of shared components, as atomic, audited
  operations.
- Published versions, publish history, rollback and copy of a version into the draft.
- A site-wide publish log for Administrators that keeps history entries of deleted pages
  and components.
- Scheduled publish and unpublish carried out by the built-in background jobs.
- A separate "Publish design" action for the site's design tokens, with history and
  rollback.
- Published pages following address changes in the page tree, with a warning in the
  admin shell before the change.
- The server-side renderer from the page schema to HTML, with one renderer per block type
  and shared components rendered in place.
- Output caching, dependency tracking and exact invalidation, across instances when a
  shared cache is configured.
- The built-in not-found page.
- Performance budget tests, new capabilities, isolation suite registration, integration
  events, admin shell screens, upgrade migration and smoke-test extension.

### Out of Scope

- Collection-driven pages and templates (#16).
- SEO metadata, sitemaps, canonical addresses and redirects from old addresses (#17).
- Integrations with CDN providers, and purging external caches.
- Draft previews rendered by the renderer, both for signed-in members and as preview
  links for people without an account (#22). The editor canvas is the preview before
  publishing.
- A site-chosen custom not-found page and other error pages beyond not-found.
- Publishing several pages as one operation (bulk or site-wide publish), and approval
  workflows.
- Comparing two versions side by side.
- Trash and restore (#21) and site export and import (#20).
- Multi-language pages.

## Assumptions

- **Components are published on their own**, not copied into each page's version. A page's
  published version refers to components by identifier, and visitors see each
  component's live version. This is what constitution Principle VIII's page-to-component
  dependency tracking assumes.
- **Page structure is not versioned**: the address, parent and position come from the page
  tree (feature 010), not from a published version. The title is part of the version.
- **Rollback does not change the draft**; the admin shell shows that the draft differs from
  the live version. Copying a version into the draft is a separate action.
- **A scheduled publish captures the content when it is scheduled**, so what goes live is
  what the member reviewed.
- **Version retention of 50** per page and component keeps storage bounded for
  frequently published pages (50 × the 1 MiB document limit at worst) while covering
  normal rollback needs. It is a default that can be raised later.
- **Schedules use the member's time zone**, shown next to the time, because no earlier
  feature gives a site a time zone. A per-site time zone can be added later without
  changing stored schedules, which are absolute points in time.
- **The built-in not-found page** is enough for this feature; letting a designer choose a
  page as the not-found page is a later enhancement.
- **Freshness target of 5 seconds** allows refreshes to travel through the background
  event dispatch rather than inside the publish itself.
- **Performance budgets** are the constitution's (Principle VIII), measured on the
  reference compose setup.

## Dependencies

- Feature 001 (foundation skeleton): module structure, outbox and background worker,
  smoke test.
- Feature 002 (identity and setup): capabilities, per-site grants, forbidden versus
  not-found rule, admin shell baselines.
- Feature 003 (media storage): the media address space that images are served from.
- Feature 009 (sites and host name resolution): site resolution on the public surface,
  base URL, disabled sites, the not-found response for unknown hosts, the isolation suite.
- Feature 010 (pages, page tree and shared components): pages, addresses, the home page,
  components, drafts, the page schema contract and the out-of-date rule.
- Feature 011 (editor core): the ten block types and their definitions, design tokens, the
  block styling the canvas uses.
- The project constitution, in particular Principles IV (pluggable cache), V (multi-site
  cache namespacing), VI (render every stored schema version), VII (draft and published
  are separate), VIII (server-side rendering, caching, invalidation and performance
  budgets), IX (encoded output) and X (golden-file tests, 80% coverage on publish and
  render).
- GitHub issue #12, which this feature implements and which the pull request closes.
