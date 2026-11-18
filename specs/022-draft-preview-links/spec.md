# Feature Specification: Draft Preview Links

**Feature Branch**: `022-draft-preview-links`

**Created**: 2026-11-19

**Status**: Draft

**Issue**: #22

**Input**: User description: "#22 Draft preview links. Problem / goal: An editor must be able to show unpublished work to a reviewer who has no account, and to preview a draft exactly as it will render. A user can open a preview of any draft page or collection item and share a time-limited link. In scope: preview rendering path that reads draft data, signed time-limited preview tokens scoped to one site and one page or item, expiry and revocation, preview bar in the rendered output, noindex on previewed pages, preview never cached in the public cache, preview of a specific past version. Out of scope: inline commenting on previews, approval workflow."

## Context

Features 010 to 016 let members build pages, shared components, collections and items, and
feature 012 renders published content for visitors. Draft and published are separate worlds
(constitution Principle VII): the public site reads only published data, and nothing a
member saves is visible to anyone outside the admin until it is published.

Today the only way to see a draft is the editor canvas (feature 011) or the item form
(feature 015). The canvas uses the same block styling as the published site, but it is not
the rendered page: it has no real document around it, no list or item data from the
renderer, no metadata, and it can be seen only by signed-in members. Feature 012 explicitly
left rendered draft previews to this feature (012 clarification of 2026-09-29), and feature
011 lists preview links for people without an account as #22.

This feature adds:

- a **member preview**: a signed-in member opens any draft page or collection item, or any
  retained past version of one, rendered by the same renderer visitors get;
- **preview links**: a member creates a secret, time-limited link to one page or item of one
  site, which anyone holding it can open without an account until it expires or is revoked;
- a **preview bar** on every preview that says what is shown and that it is not live;
- protections so that previews never reach search engines, the public cache or visitors of
  the published site.

Principle VII still holds for visitors: the public path keeps reading only published data,
and previews are a separate path that only members and holders of a valid link reach.
Principle VIII's cache rules are unaffected because previews are never stored in the public
cache. Principle IX sets the security bar for links that grant access without an account.

Out of scope: inline comments on previews, an approval or sign-off workflow, previewing a
shared component, menu or the design on its own, and password-protected links.

## Clarifications

### Session 2026-11-19

- Q: When a draft changes after a preview link was created, does the link show the latest
  saved draft or the draft as it was when the link was created? → **A: the latest saved
  draft.** One link serves a whole review round; the preview bar shows when and by whom the
  draft was last saved (FR-011, FR-020).
- Q: Do page previews show shared components, the site design and menus in their published
  versions or their drafts? → **A: published versions, with drafts only for parts that were
  never published, marked in the preview bar.** The preview shows what visitors would get if
  only this page were published, and a new site can be previewed before anything is
  published (FR-029, FR-020).
- Q: Which site members may create preview links for people without an account? → **A: every
  member who may edit the draft: Administrator, Designer and Editor.** Creating a link
  publishes nothing, so the publish capability is not required (FR-005).
- Q: When a link's creator loses access to the site, do their links keep working? → **A: no,
  all their links stop working at once.** An Administrator who needs a link to continue
  creates a new one (FR-009).
- Q: How long does a preview link last by default, and what is the longest a member may
  choose? → **A: 7 days by default, 30 days at most.** Links never last forever and cannot be
  extended; members create a new link (FR-005).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A member previews a draft exactly as it will render (Priority: P1)

Lan, an Editor, has rewritten the "Pricing" page, which is already live. Before asking for it
to be published she chooses "Preview" in the editor. A new browser tab opens with the page
as a complete rendered document: the site's header and footer, the site's design, the
collection list further down filled with published plans, the page title in the browser tab.
A bar across the top says "Preview of draft, saved 10:42 by Lan — not live". The public
address `https://example.com/pricing` still shows the old prices.

**Why this priority**: Without a rendered draft, nothing else in this feature exists. It is
also useful on its own: members finally see a draft exactly as visitors will, including
parts the canvas does not show.

**Independent Test**: Build a page that uses every block type, a shared component and a
collection list, and save a draft that differs from its published version. Open the member
preview and confirm that, apart from the preview bar, it matches the HTML the renderer
produces for the same content once published. Confirm that the public address still serves
the published version and that the public cache entry for the page is unchanged.

**Acceptance Scenarios**:

1. **Given** a page with a saved draft,
   **When** a signed-in member of the site opens its preview,
   **Then** they see a complete document rendered from the draft by the same renderer that
   serves published pages, with the preview bar above the content.
2. **Given** a collection item with a saved draft and a collection template,
   **When** a signed-in member opens the item's preview,
   **Then** they see the item's draft values rendered through the template, as its item page
   would look once both are published.
3. **Given** a page that has never been published,
   **When** a member opens its preview,
   **Then** the preview works the same way, and the public address still answers not found.
4. **Given** a draft with unsaved changes in the editor,
   **When** the member opens the preview,
   **Then** the latest changes are saved first (or the member is told the preview shows the
   last saved draft), so the preview never silently shows an older state than the editor.
5. **Given** a preview is open,
   **When** the member saves further changes and reloads the preview,
   **Then** the preview shows the new draft.
6. **Given** a signed-in user who is not a member of the page's site,
   **When** they try to open the member preview,
   **Then** they get the not-found response, without learning whether the page exists.

---

### User Story 2 - A member shares a time-limited preview link with a reviewer (Priority: P1)

Lan wants the client to check the new prices. The client has no account. From the page she
chooses "Share preview", keeps the default expiry of 7 days and copies the link. The client
opens it on their phone and sees the rendered draft with a bar saying "Preview — not live.
This link expires on 26 Nov". The client cannot reach any other draft with it, and once the
link expires the page shows "This preview link is no longer available".

**Why this priority**: This is the problem the issue states: showing unpublished work to
someone without an account. Today members send screenshots or publish early.

**Independent Test**: Create a link for a draft page, open it in a browser with no session,
and confirm the rendered draft appears. Change the page address or token to point at another
page, another site, or a published-only page, and confirm each request gets the same "no
longer available" response with no content. Let the link expire (or move the clock) and
confirm it stops working.

**Acceptance Scenarios**:

1. **Given** a draft page or item,
   **When** a member who may edit it creates a preview link and chooses an expiry,
   **Then** they get a link they can copy, and the link appears in the list of the page's or
   item's active links with its creator, creation time, expiry and an optional label.
2. **Given** a valid link,
   **When** anyone opens it without signing in,
   **Then** they see the rendered preview of that one page or item with the preview bar,
   showing when the link expires.
3. **Given** a valid link for one page,
   **When** someone alters it to try to reach another page, another item, another site or a
   past version it was not created for,
   **Then** they get the "no longer available" response and see no content.
4. **Given** a link whose expiry has passed,
   **When** anyone opens it,
   **Then** they get the "no longer available" response, which does not reveal the page's
   title, address or whether it still exists.
5. **Given** a reviewer viewing a link,
   **When** they follow a link inside the previewed page to another page of the site,
   **Then** they reach that page's public, published address (or its not-found page), never
   another draft, and the bar's text made this clear beforehand.
6. **Given** a link for a page whose draft has changed since the link was created,
   **When** the reviewer opens or reloads it,
   **Then** they see the latest saved draft, and the preview bar shows when and by whom it
   was saved.

---

### User Story 3 - Previews stay out of search engines, caches and the public site (Priority: P1)

Lan's draft contains a price that is not public yet. The link she sent to the client gets
pasted into a chat tool, which fetches it to show a link card, and a search engine crawler
later finds it. Neither may index the page, and nobody who visits `https://example.com/pricing`
ever sees the draft, even if a preview of it was just rendered.

**Why this priority**: A preview that leaks into search results or the public cache breaks
Principle VII for real visitors. This is a safety property of Stories 1 and 2, not a
separate capability, so it ships with them.

**Independent Test**: Open member and link previews of a page, then request the page's public
address and confirm it serves the published version from the public cache, and that the
cache holds no preview output. Inspect every preview response and confirm it tells crawlers
not to index or follow it, tells browsers and intermediate caches not to store it, and that
following an external link from a preview does not pass the preview address to that site.

**Acceptance Scenarios**:

1. **Given** any preview response (member or link, draft or past version),
   **When** it is served,
   **Then** it tells search engines not to index it or follow its links, whatever the page's
   own search setting is.
2. **Given** a page with a cached published version,
   **When** previews of its draft are opened any number of times,
   **Then** the public address keeps serving the published version, and no preview output
   is ever written to or read from the public cache.
3. **Given** a preview response,
   **When** it is served,
   **Then** it tells browsers and intermediate caches not to store it, so a shared proxy
   cannot hand it to someone else.
4. **Given** a reviewer on a preview,
   **When** they follow a link to another website,
   **Then** the preview link is not passed to that website.
5. **Given** a page or item that is previewed,
   **When** the sitemap and robots responses of feature 017 are produced,
   **Then** previews never appear in them.
6. **Given** a previewed page that contains a form (feature 018),
   **When** a reviewer submits it,
   **Then** nothing is stored as a submission and no notification is sent; the form says
   it is a preview.

---

### User Story 4 - A member revokes links and sees who has access (Priority: P2)

The client forwards Lan's link to someone it should not have reached. Lan opens "Share
preview" on the page, sees two active links with their labels, expiries and when each was
last opened, and revokes the client's link. The next request with it gets "no longer
available". An Administrator later reviews every active link on the site and revokes the
ones nobody needs.

**Why this priority**: Expiry alone leaves a link usable for up to its full lifetime.
Revocation is the safety valve, but the feature still delivers value before it exists
because links expire.

**Independent Test**: Create several links for a page and items, revoke one and confirm only
that one stops working at once, also on a second application instance. Remove the creator's
role on the site and confirm their links stop working. Confirm the site-wide list shows every
active link with its target.

**Acceptance Scenarios**:

1. **Given** a page or item with active links,
   **When** a member who may edit it opens its sharing panel,
   **Then** they see each active link's label, creator, creation time, expiry and when it was
   last opened, and can revoke one or all of them.
2. **Given** a revoked link,
   **When** anyone opens it, on any application instance,
   **Then** they get the "no longer available" response from the next request on.
3. **Given** an Administrator of a site,
   **When** they open the site's preview links list,
   **Then** they see every active link of the site with its target page or item, creator and
   expiry, and can revoke any of them.
4. **Given** a member whose access to the site is removed,
   **When** a link they created is opened afterwards,
   **Then** it no longer works.
5. **Given** creating and revoking links,
   **When** either happens,
   **Then** it is recorded in the page's or item's history with who and when (never the link
   itself).

---

### User Story 5 - A member previews a specific past version (Priority: P2)

A Designer is about to roll the "Pricing" page back to version 7 but first wants to see it.
From the publish history they choose "Preview" on version 7 and see it rendered with today's
header, footer and design, which is exactly what a rollback would publish. They also share a
link to version 7 with the client, who confirms it is the right one.

**Why this priority**: Rollback (features 012 and 015) is a live action on the public site.
Seeing a version first avoids rolling back to the wrong one, but drafts are the main use.

**Independent Test**: Publish three versions of a page, preview each from the history, and
confirm each shows its own content. Roll back to one and confirm the published result matches
its preview. Create a link for a version, then let that version fall out of retention and
confirm the link stops working.

**Acceptance Scenarios**:

1. **Given** a page or item with retained published versions,
   **When** a member opens the preview of one of them from its history,
   **Then** they see that version's content rendered as a rollback to it would publish it now,
   with the bar naming the version number, when and by whom it was published.
2. **Given** a past version,
   **When** a member creates a preview link for it,
   **Then** the link shows only that version, never the draft or another version.
3. **Given** a link to a version that is no longer retained,
   **When** anyone opens it,
   **Then** they get the "no longer available" response.

---

### Edge Cases

- **Shared components, design, menus and referenced content in a preview**: a page preview
  shows the page's own draft (or chosen version) together with the live published versions
  of shared components, the site design and menus, which is what visitors would get if only
  this page were published. A component, menu or design that has never been published is
  shown from its draft, and the preview bar names each part shown from a draft (so a new
  site can be previewed before anything is published). Collection lists and references to
  other items show published items only, exactly as on the public site.
- **Item without a template, or a template page preview**: an item whose collection has no
  template cannot be previewed as a page; the preview action says why instead of failing.
  A template page's own preview renders with a sample item chosen by the member (by default
  the editor's sample item, feature 016), using that item's published values or, if it has
  none, its draft values, and the bar names the item.
- **Invalid draft**: a page draft that fails validation against the page schema cannot be
  rendered reliably. The member preview names the problem and where it is; a link shows
  "This preview is not available right now" with no details, and starts working again once
  the draft is fixed. An item that needs attention (a required value missing or a
  constraint unmet, feature 015) is still previewed, and the bar warns that some values may
  not show, so a reviewer can help find the gaps.
- **Page or item moved to the trash** (feature 021): its previews and links stop working while
  it is in the trash, like everything else. After a restore, unexpired links that were not
  revoked work again. A permanent delete or purge ends them for good.
- **Address changes**: a link identifies the page or item, not its address, so it keeps
  working after a slug change or a move in the page tree. Paths inside the preview use the
  draft's addresses.
- **Site without a host name** (feature 009): member previews and links still work, because
  previews do not depend on the site's public host name.
- **Site or collection deleted**: all their links stop working.
- **Two instances**: creating, revoking and expiring links take effect on every instance
  within the same few seconds as other cross-instance changes.
- **Guessing and brute force**: a link cannot be guessed or derived from another link, and
  repeated requests with invalid links are slowed down so they cannot be used to probe.
- **Many links**: a page or item may have up to 20 active links; creating more asks the
  member to revoke one first.
- **Expiry while viewing**: a reviewer who already has the preview open keeps what is on
  screen; the next request (reload, navigation) gets "no longer available".
- **Media in drafts**: images and files the draft uses are shown, including ones that no
  published page uses yet, but the link grants access only to files that the previewed
  content uses.
- **Scheduled publish**: previewing the draft of a page with a pending scheduled publish shows
  the current draft; the bar mentions the schedule so the reviewer knows when it goes live.
- **Operator emergency**: the operator can make every preview link of the installation stop
  working at once (for example after a link list leaked), without affecting anything else.
- **Export and import** (feature 020): preview links are not exported; an imported site has
  none.

## Requirements *(mandatory)*

### Functional Requirements

**Member preview**

- **FR-001**: Signed-in members of a site MUST be able to open a rendered preview of the saved
  draft of any page of that site and of any collection item that has a template, from the
  editor, the page list, the item form and the item list.
- **FR-002**: The preview MUST be produced by the same renderer, block renderers, design and
  encoding rules that serve published pages (feature 012 FR-022 to FR-025 and feature 016),
  so that it is identical to what publishing the same content would serve, apart from the
  preview bar, the preview-only response rules below, and addresses of media, the
  stylesheet and links rewritten so they work from the preview (FR-012, FR-027), and forms
  that post to the preview instead of sending (FR-026).
- **FR-003**: Opening a preview from the editor or item form MUST either save pending changes
  first or tell the member the preview shows the last saved draft.
- **FR-004**: Users who are not members of the site MUST get the same not-found response as
  for a page that does not exist.

**Preview links**

- **FR-005**: Members who may edit a page's or item's draft (Administrator, Designer and
  Editor) MUST be able to create a preview
  link for its draft or for one of its retained published versions, with an optional label
  and an expiry chosen from 1 hour, 1 day, 7 days (default) or 30 days, or a date and time up
  to 30 days ahead. A link to a template page also names the sample item it shows.
- **FR-006**: A preview link MUST grant access to exactly one site, one page or item, and one
  target (the draft, or one specific version), and nothing else. Changing any part of the
  link MUST NOT reach other content.
- **FR-007**: Preview links MUST be impossible to guess or forge, and the link MUST NOT
  contain the page's title, address, or any content.
- **FR-008**: Anyone holding a valid link MUST be able to open the preview without an account
  or sign-in.
- **FR-009**: A link MUST stop working at its expiry, when revoked, when its target page or
  item is moved to the trash (until restored), deleted or its site or collection deleted,
  when its target version is no longer retained, and at once when its creator loses edit
  access to the content on that site (removed from the site, or a role change).
- **FR-010**: Every request with an invalid, expired, revoked or altered link MUST get the
  same "This preview link is no longer available" page, which reveals nothing about the
  content and carries the not-found status.
- **FR-011**: When a link's target draft changes after the link was created, opening the link
  MUST show the latest saved draft at the time of each request. Links to a past version always
  show that version.
- **FR-012**: Links on previewed pages to other pages and items of the site MUST lead to their
  public addresses; a link MUST NOT open any content other than its own target.
- **FR-013**: Repeated requests with invalid links from the same source MUST be slowed down
  or refused for a while, in line with the product's other rate limits.

**Managing links**

- **FR-014**: The sharing panel of a page or item MUST list its active links with label,
  creator, creation time, expiry and last-opened time, and let members who may edit the
  content revoke one or all of them. The full link MUST be shown only once, when created.
- **FR-015**: Site Administrators MUST be able to list every active link of their site with
  its target and revoke any of them.
- **FR-016**: Creating and revoking a link MUST be recorded in the target's history with who,
  when, target and expiry, never the link itself.
- **FR-017**: Revocation and expiry MUST take effect on every application instance within 5
  seconds.
- **FR-018**: A page or item MUST NOT have more than 20 active links at a time.
- **FR-019**: The operator MUST be able to make every preview link of the installation stop
  working at once through configuration, without affecting any other data.

**Preview bar**

- **FR-020**: Every preview MUST show a bar, outside the page content, that states it is a
  preview and not live, what it shows (draft with save time and author, or version number
  with publish time and author), for a template page the sample item, any shared component,
  menu or design shown from its draft because it was never published, any pending scheduled
  publish, and for link viewers the expiry. For a member preview it MUST also link back to
  the editor or item form and to the sharing panel.
- **FR-021**: The bar MUST NOT change the layout of the page below it at any preview width
  (feature 011's desktop, tablet and mobile), and the viewer MUST be able to hide it for the
  current view to check the page without it.

**Isolation from the public site**

- **FR-022**: The public rendering path MUST stay unchanged: it reads only published data
  (constitution Principle VII) and never serves preview output.
- **FR-023**: Preview output MUST NEVER be written to or read from the public render cache,
  and rendering a preview MUST NOT discard or refresh any public cache entry.
- **FR-024**: Every preview response, and the "no longer available" page, MUST tell search
  engines not to index it or follow its links, and MUST tell browsers and intermediate caches
  not to store it, whatever the content's own search settings are.
- **FR-025**: Preview pages MUST NOT pass their address to other websites when a viewer
  follows a link, and MUST NOT be listed in the sitemap or any other public listing.
- **FR-026**: Forms in a preview MUST NOT store submissions or send notifications; submitting
  one MUST tell the viewer that this is a preview.
- **FR-027**: A link MUST give access only to the media files the previewed content uses.

**Past versions**

- **FR-028**: Members MUST be able to preview any retained published version of a page or
  item (features 012 FR-013 and 015 FR-023) from its history. A version preview MUST show the
  version's content as a rollback to it would publish it at that moment.

**Content in previews**

- **FR-029**: Collection lists, references to other items and anything else the page reads
  from other content MUST show only published data, as on the public site. Shared
  components, the site design and menus MUST appear in their live published versions; one
  that has never been published MUST appear from its draft.
- **FR-030**: A page draft that cannot be rendered because it fails page schema validation
  MUST give members the reason and location, and link viewers a neutral "not available right
  now" page. An item that needs attention MUST still be previewed, with a warning in the bar.

**Security and logging**

- **FR-031**: Authorization for member previews and for creating, listing and revoking links
  MUST be checked on the server for every request (constitution Principle IX).
- **FR-032**: Logs MUST refer to links, pages, items and versions by identifier only; they
  MUST NOT contain link secrets or content.

### Key Entities

- **Preview**: a rendering of one page or item of one site from either its saved draft or one
  retained published version, for a signed-in member or for a link holder. Never stored in
  the public cache.
- **Preview link**: grants a person without an account access to one preview. Has a site, a
  target page or item, a target state (draft, or a version number), an optional label, a
  creator, a creation time, an expiry, a last-opened time and a revoked state with who and
  when. Its secret is shown once and is never stored or logged in readable form.
- **Preview bar**: the band shown above every preview that tells the viewer what they see.
- **History entry** (extends features 012 and 015): records link creation and revocation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every block type and for item pages, a member preview of a draft matches the
  published output of the same content, apart from the preview bar, preview-only response
  rules, rewritten addresses and forms (FR-002), in 100% of golden-output comparisons.
- **SC-002**: A member goes from the editor to a shareable link in under 30 seconds, and a
  reviewer without an account sees the preview on first try, on desktop and phone.
- **SC-003**: Previews appear within 1 second for 95% of requests on the reference setup.
- **SC-004**: In a test that checks every preview response, 100% tell search engines not to
  index and caches not to store, and 0 preview outputs are found in the public cache or served
  at a public address.
- **SC-005**: 0 of at least 1,000 altered, expired, revoked, cross-site and cross-content
  links show any content.
- **SC-006**: A revoked link stops working on every instance within 5 seconds.
- **SC-007**: After a rollback, the published page matches the preview of the restored version
  in 100% of cases.
- **SC-008**: In the acceptance walkthrough, a member shows a never-published page and a
  never-published item to a reviewer without an account, and the public site answers not
  found for both throughout.

## Assumptions

- **Who may create links**: every member who may edit the draft (Administrator, Designer and
  Editor; clarified 2026-11-19). The publish capability (feature 012 FR-009) is not required.
- **Who may revoke**: the members who may edit the content, for that content's links, and site
  Administrators for every link of their site.
- **Expiry**: 7 days by default and 30 days at most (clarified 2026-11-19). An expiry cannot
  be extended; members create a new link.
- **Reviewers only view**: no comments, approvals or sign-off (out of scope as stated).
- **Link address**: a link opens on the site's own host name; for a site without one, on the
  address members use for the admin.
- **Bar language and times**: the preview bar and the "no longer available" page are in
  English at first, and show times in UTC, because a page without scripts cannot know the
  viewer's time zone. Translations come later.
- **Components, menus and the design are previewed through a page**, not on their own.
- **Lists and references show published data**, matching feature 016's rule for lists in the
  editor canvas.
- **A link ends with its creator's access** (clarified 2026-11-19); Administrators who want a
  link to outlive a member create a new one.
- Features 009, 010, 011, 012, 015, 016, 017, 018, 021 and 023 are in place.

## Out of Scope

- Inline comments, annotations or feedback forms on previews.
- Approval workflow, sign-off or publishing from a preview.
- Password-protected or person-specific links (links are bearer links).
- Previewing a shared component, a menu or the design on its own.
- Previews of a whole draft site with navigation between drafts.
- Exporting or importing preview links.

## Dependencies

- Feature 002 (identity): site membership, roles and the publish capability.
- Feature 009 (sites and host names): site resolution; previews work without a host name.
- Features 010 and 011 (pages, components, editor): drafts, preview widths, schema validation.
- Feature 012 (publishing and rendering): renderer, versions, retention, history, public cache.
- Features 015 and 016 (items, templates and binding): item drafts and versions, templates,
  sample items, lists.
- Feature 017 (SEO): search settings, sitemap and robots responses that previews must stay out of.
- Feature 018 (forms): forms that must not store submissions from previews.
- Feature 021 (trash): content in the trash is unreachable through previews.
