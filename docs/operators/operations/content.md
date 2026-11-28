# Content and publishing

How pages, the editor, collections, templates and menus behave for an operator: what is
public, what is cached, and the limits that apply.

## Pages


Each site has a tree of pages and a set of shared components (a header, a footer), managed
under **Sites → a site → Pages** and **Shared components** in the admin shell. Designers
build the structure and the components; editors can see them and save page drafts.

### Drafts and what visitors see

Visitors see only what has been published (see [Content and publishing](content.md#publishing-and-rendering)). Saving a
draft, renaming or adding a page changes nothing public until someone publishes. Moving or
renaming a *published* page is the exception: it changes that page's address at once, and
the admin shell warns first.

### Addresses the platform keeps

No page address can begin with `/api`, `/admin` or `/health`: the platform answers there
before any page could. `/_media` is safe by construction, because slugs cannot contain
`_`. The list is part of the page schema contract
([page schema contract](https://github.com/trungnd1985/quick-builder/blob/main/docs/contracts/page-schema.md) §5).

### Limits

A site holds at most 2,000 pages, nested at most 10 levels deep. A page's content document
is at most 1 MiB and 5,000 elements. These are part of the page schema contract, not
settings: a site valid on one installation is valid on every other, which export and
import rely on.

### Deleting

Deleting a page or a component moves it to the site's [trash](trash.md), where it can be
restored until it is purged. A page with child pages, the home page while other pages exist, and a component that pages still use
cannot be deleted — the admin shell says why.

## Editor and design


Pages and shared components are built in the visual editor: **Pages → a page → Edit
content**, and **Shared components → Edit content**. Each site's colours, fonts, spacing and
corner radii are under **Sites → a site → Design**. Designers edit components and the design;
editors build page content, and can give any block a border and rounded corners from those
tokens. Corner radii are part of the design, so they travel with site exports like the rest.

### Drafts and the design

The editor saves drafts only. Visitors see a page's changes when it is published, and
design changes when the design is published (**Design → Publish design**), not when they
are saved.

### Nothing leaves the installation

The editor makes no request to any other host. Video embeds show a placeholder naming
YouTube or Vimeo rather than loading the player, and fonts are system font stacks, so no
font is downloaded. It works on an installation with no internet access.

### What the browser keeps

- **Unsaved changes.** While changes wait to be saved, a copy is kept in the browser's
  IndexedDB, per user and per page, so a crash or a closed tab loses nothing: the next visit
  offers them back. The copy goes as soon as the changes are saved, when the offer is
  declined, and when the user signs out.
- **Copied blocks** are kept in the browser's local storage, so they survive a reload and
  paste into other pages of the same site. Signing out clears them.
- **The preview width** each user last chose.

On a shared computer, users should sign out rather than close the window: a session that
simply expires keeps that user's copies (only that user is ever offered them).

### Two people on one page

Everyone who opens a page, component or item sees who else has it open. The first person
to change it holds it, and the others see it read-only, with who is editing and since when,
until it is free. A holder who has changed nothing for 30 minutes can be taken over; the
takeover is recorded (`editor.editing_takeovers`, kept 90 days, ids only). Leaving the
editor, closing the tab or signing out frees it at once; a crashed browser or lost network
frees it within 2 minutes.

A save made after someone else's is still refused, never overwritten, and now says who
saved and when. The member can reload the latest version, keep viewing theirs read-only to
copy from it, or save it as a new unpublished page, component or item.

- **Request rate**: each open editor or item form sends a small request every 10 seconds
  (and lists of pages, components and items one every 15 seconds while shown). A reverse
  proxy that rate-limits `/api/v1/` must allow this.
- **Several instances**: locks live in PostgreSQL, so every instance agrees on who holds a
  document with no shared cache configured.
- **Old tabs**: an admin tab open from before the upgrade has no lock, but its saves are
  still refused rather than overwriting. Reload it to get presence and locks.

## Publishing and rendering


Visitors get server-rendered HTML of what has been published: pages, the shared components
they show, and the site's design. Designers and Administrators publish; Editors prepare
drafts.

### Publishing

- **Pages and components** are published from their publishing menu in the page tree, the
  component list and the editor. A page can only be published once the components it
  shows are; a component that published pages show cannot be unpublished or deleted.
- **The design** is published on its own, from **Design → Publish design**. Until the first
  design publish, pages use the default design.
- **Unpublishing** takes a page off the site from the next request. A published page
  cannot be deleted until it is unpublished.
- **History**: every page and component keeps its 50 most recent versions, which can be
  viewed, rolled back to (published again as a new version) or copied into the draft. The
  design keeps 50 versions too.
- **The publish log** (**Sites → a site → Publish log**, Administrators only) lists every
  publishing action on the site, including those on pages and components that have since
  been deleted.

### Schedules

A publish of a page or component, or an unpublish of a page, can be scheduled. A scheduled
publish captures the draft when it is set, so what goes live is what was reviewed. The
schedule is a delayed message in the Pages outbox and runs within one outbox poll interval
(`Outbox:PollInterval`, 5 s by default) of its time, exactly once, on whichever instance
claims it. A stopped installation runs overdue schedules when it starts again. A schedule
that cannot run, for example because a component it needs was unpublished in the meantime,
is marked failed and logged, and the admin shell shows it until someone acts.

A manual publish, unpublish or rollback cancels the matching pending schedule; the admin
shell says so before it does.

### Caching and memory

Rendered pages are cached in each instance's memory, up to `Cache:MemoryLimitMegabytes`
(default `256`, from 16 to 16,384). The least recently used pages go first when it is full,
and a page not requested for an hour is dropped. A typical page is tens of kilobytes, so the
default holds several thousand. Size the container's memory limit for this cache on top of
the application's own needs.

The cache never serves stale content: each page's key includes numbers that every relevant
change moves on, in the same transaction as the change, and they are read from the database
on every request. So a publish is visible on every instance at its next request, with no
shared cache and nothing to purge (plan exception E-4 closed). Visitors' browsers get an
`ETag` and `Cache-Control: public, no-cache`, so they revalidate each time and get a `304`
while nothing has changed. A CDN in front must respect those headers.

If the cache fails, pages are rendered without it: slower, never wrong.

### Public paths

- `/_assets/blocks.{hash}.css` is the block stylesheet, served on every site's host names
  and cached by browsers for a year; a new release changes its name.
- `/_media/` serves media files, as before.
- Any other path on a site's host is a page address, or the site's not-found page. Only
  `GET` and `HEAD` are answered; other methods get `405`.

### The not-found page

Missing addresses get an HTML page in the site's design, with a heading and a sentence in
the site's default language (all 40 supported languages; English where a language has no
text) and a link home while the home page is published. It is never cached by browsers
(`Cache-Control: no-store`) and contains nothing from the request. Unknown and disabled host
names still get the JSON `404` of feature 009.

### Database connections

The public path reads through read-only connections (`default_transaction_read_only=on`),
so a bug there cannot write. That adds one connection pool per reading module — Pages,
Editor and Media — to the one each module already has. With Npgsql's default of up to 100
connections per pool, set PostgreSQL's `max_connections` for the pools you expect under
load per instance, times the number of instances; in practice a few dozen per instance
suffice.

## Collections


Each site can define collections: structured, repeatable content such as blog posts or
products, managed under **Sites → a site → Collections**. Administrators and designers
define a collection's fields; editors can see them. Every site role, editors included,
writes, publishes and schedules a collection's **items** (feature 015), from the
collection's **Items** link. Pages show items through templates and collection lists (016,
below).

### Changing fields after items exist

- **Renaming, re-keying and reordering** fields never touch stored items. Items are stored
  by field id, which never changes.
- **Removing a field or an option, and changes that convert values** (text to number, rich
  text to text, fewer decimal places, …) show a preview first. It says how many items are
  affected and how. Values removed or shortened this way are **deleted for good**: there is
  no undo. Back up the database before a large restructuring.
- A change applies to items' **draft and published** values at once. While it runs, edits
  to that collection's items are refused with "try again shortly"; reading is never blocked.
- Conversions of dates use **UTC**, because sites have no time zone yet.
- A change that removes or converts values also applies it to the **published versions kept
  for rollback** and to content captured for a **scheduled publish**, so a removed value is
  gone from everywhere.

### Large collections and reverse proxies

A change that rewrites many items runs inside one request (about 30 seconds at 10,000
items on the reference setup). Give the admin path a proxy timeout of at least **60
seconds**. If the connection drops, the change rolls back completely and can simply be
saved again.

### Deleting

Deleting a collection deletes its fields, history and every item, permanently; collections
do not go to the trash. A collection that another collection's reference field points at cannot
be deleted until that field is removed or changed.

### Items

- **Drafts and publishing**: an item's draft can be saved however incomplete; publishing
  needs every required value and every value within its field's settings. Each publish is a
  version; the last **50** are kept, and any of them can be published again or copied into the
  draft.
- **Who may publish**: unlike pages, **editors may publish, unpublish and schedule items**.
- **Schedules** run through the same built-in background jobs as page schedules, within a
  minute of their time, once, also after a restart. A scheduled publish whose content no
  longer meets the fields' rules when its time comes does not publish; the item's history
  says why and the item is marked as needing attention.
- **Deleting an item** moves it to the [trash](trash.md); an item others refer to cannot be
  deleted, and the refusal lists them.
- **Media**: a file used by a **published item** counts as in live use, so only designers and
  administrators can delete or replace it, as for files on published pages.

### Rich text

- **Formats**: rich text, in Rich Text blocks and in items' rich text fields, holds headings
  (levels 2 to 4), quotes, bulleted and numbered lists, bold, italic, underline,
  strikethrough, inline code, links, images from the media library, simple tables and
  alignment. It is always stored as structured data, never as HTML.
- **Images need alternative text** before a page, component or item is published, or must
  be marked decorative. Drafts can be saved without it; publishing (manual or scheduled)
  names the images that need it.
- **Pasting**: content pasted from a word processor or a web page keeps its headings, lists,
  tables and text styles; colours, fonts and anything else are dropped. Images that exist
  only as web addresses are left out, with a notice; pasted image files (screenshots) are
  uploaded to the media library's top folder.
- **Limits** per rich text: 20,000 characters, 50 images and 20 tables; a table holds at most
  200 rows and 20 columns.
- **Collections**: a rich text field can limit its formats (headings and quotes, images,
  tables). Turning a group off keeps existing content and marks the items that use it as
  needing attention.
- **Media**: images in rich text count as uses of their file, like Image blocks and image
  fields.

## Templates and collection lists


Feature 016 puts collection items on the site.

### Item pages and addresses

- A designer makes a page the **template** of a collection, under **Pages → a page → Use as
  template**. Every **published** item then has a page at the template's parent address
  followed by its slug: a template under `/blog` answers `/blog/{slug}`. A collection has
  at most one template, and a parent holds at most one.
- Addresses are matched in this order: the platform's own paths (`/_media`, `/_assets`,
  `/api`, `/admin`), then a live page at that address, then a live template with a
  published item of that slug, then the site's not-found page. So a page created at
  `/blog/archive` hides the item whose slug is `archive`; the admin warns when that
  happens.
- An item's page exists while the item is published and the template is live. Changing an
  item's slug, moving the template or an ancestor, or unpublishing the template changes or
  removes item addresses; the admin says how many. Old addresses answer with the not-found
  page unless the member keeps them as [redirects](seo.md#redirects).

### Lists, paging and caching

- A **collection list** block shows published items with filters, a sort and a limit, or
  page by page with `?page=N` (each list on a page has its own parameter name). Paging
  links are plain links: they work without JavaScript and can be shared.
- Only a page's own list parameters, with page numbers from 2 to 10,000, become part of the
  cache key; every other query parameter is ignored, so tracking parameters cannot fill the
  page cache.
- Pages refresh exactly when what they show changes: publishing an item re-renders its own
  page and the pages that list its collection, or a collection that refers to it; a
  collection's schema change re-renders every page that shows the collection, item pages
  included. Re-rendering happens at the next request.
- **Over-refresh on busy collections**: a template that holds a list of its own collection
  ("related posts") re-renders every item page whenever any item of that collection is
  published. This is correct, and each page re-renders only when next visited, but on a site
  that publishes often it lowers the cache hit rate. Lists on the home page of a busy
  collection behave the same way.
- One page render makes at most 200 list queries; lists beyond that show their empty state,
  and a warning naming the page and list ids is logged.

### Deleting collections and changing fields

- A collection that a page or component uses, in its draft, live version or a scheduled
  publish, **cannot be deleted**. The refusal lists them (up to 50) and says where each uses
  it; remove the lists, bindings or template first.
- A schema change's preview says how many pages and components read each field it removes or
  retypes. After the change, live pages show the new data at their next request, and a
  binding to a removed field shows the block's own value (or hides the block, if the
  designer chose that).
- Nothing a collection change does can make a page fail: broken bindings fall back, broken
  filters match nothing, and the page still answers.

## Navigation and menus


Designers build each site's menus under **Menus** (up to 50 per site, 300 entries over three
levels each) and show them with the Navigation block. Menus are published like components:
a menu publish refreshes exactly the pages that show it, directly or through a component, and
a page, item or template change refreshes the pages whose menus point at it.

- **Content-Security-Policy**: a page that shows a Navigation block carries one small inline
  script, admitted by its hash: that page's policy adds `script-src 'sha256-…'`. Other pages
  carry no script and no `script-src`. A reverse proxy or CDN that rewrites or replaces the
  policy must keep that `script-src` on those pages, or menus lose Escape and collapse
  handling. They stay usable either way: every submenu is a native disclosure, and the
  small-screen panel is written open.
- **Menu styles**: the site-wide look and colours of menus are set in **Design**, under
  Menus, and reach visitors when the design is published. Each Navigation block can override
  any of them. Sites that set no menu style render exactly as before it existed. Colours that
  are hard to read are warned about in the admin, never refused.
- Menu names, labels and addresses are content: they never appear in logs or in the menu
  audit, which records ids and actions only.
