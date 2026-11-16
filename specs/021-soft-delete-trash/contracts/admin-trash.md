# Admin Behaviour: Trash

How the admin presents the trash. Research: TR-10, TR-11, TR-14, TR-16, TR-19 and TR-20.
All visible text goes in `i18n/en.json`. Every journey works with the keyboard alone and
passes axe (FR-031).

## 1. Delete dialogs (changed)

This applies to pages, components, items and media.
- **Confirmation text**: "{Name} will be moved to the trash. It can be restored until
  {purgeDate}." The purge date is today plus `trashRetentionDays`, a new field of the site
  response (`GET /api/v1/sites/{siteId}`, which every member of the site may read). After
  the delete, the receipt's `purgeOnUtc` is shown (FR-002).
- **Existing guards and warnings stay**: children, home page, published page, component in
  use, item referenced, media in use, publish capability.
- **New refusal**: `editing.lock_held` reads "{Name} is editing this page since {time}. You
  can delete it when they have finished, or once their lock is idle."
- **After the delete**: a toast says "Moved to the trash" and links to the trash view.

## 2. Trash view: `/sites/:siteId/trash`

- **Navigation**: the site's navigation shows a "Trash" entry when the member may view at
  least one kind.
- **Tabs**: Pages, Components, Items and Media, each shown only with its view capability.
  The selected tab is kept in the URL (`?tab=`).
- **List**: newest first, 50 per page with "Load more". Each row shows:
  - the label;
  - the address or file name;
  - where it was, with a "(in trash)" or "(gone)" marker on the parent or folder;
  - who deleted it and when;
  - "Deleted permanently on {date}";
  - "Was live" or "Was scheduled" badges.
- **Filters**: search box (debounced 300 ms), deleted by, date range, collection (Items) and
  folder (Media).
- **Media total**: the Media tab shows "{n} files, {size} in the trash".
- **Row actions**:
  - Preview (§3);
  - Restore, when `canRestore`;
  - Delete permanently, when `canPurge`.
- **Multi-select**: select rows, then "Restore selected" or (Administrators) "Delete
  selected permanently".
- **Empty trash**: Administrators see this button. It opens §5.
- **States**:
  - empty: "Nothing in the trash. Deleted pages, components, items and files stay here for
    {n} days.";
  - loading: skeleton rows;
  - error: retry.

## 3. Preview

- **Pages, components and items**: the text of the draft, read-only, with no presence or locks.
- **Media**: the file's name.
- **Banner**: every preview has "In the trash — deleted by {name} on {date}" and, where the
  member may restore, a Restore button.

The editor canvas, the read-only item form and an image of a trashed file are deferred
(`docs/backlog.md`).

## 4. Restore dialog

1. Opens with `GET …/restore-plan` (placement `original`).
2. Shows where the content will go:
   - "Back under {parent}"; or
   - when the parent is in the trash, a choice between "Restore {parent} as well" (listing
     the ancestors) and "Choose another place" (a tree picker, top level preselected); or
   - when the parent is gone, only the picker.

   Changing the choice re-fetches the plan.
3. Shows changes: "The address {old} is taken; it will be restored as {new}", "It will
   become the home page", "It will no longer be the template for {collection}", or the
   schema issues for items ("It will be marked as needing attention: …").
4. When `wasLive` or `hadSchedule` is set, shows "This was live when it was deleted" or "This
   was scheduled to publish". With `canRestoreAndPublish`, the buttons are "Restore" and
   "Restore and publish"; otherwise only "Restore".
5. Confirming sends the restore with `confirmedAddress`. On `trash.plan_changed` it shows
   the new plan with a note: "Something changed while you were deciding."
6. Success says "Restored. Open {label}" and links to the editor, item form or library.

Bulk restore fetches the plans of the selected entries first. Entries that need a choice
(parent in the trash or gone) are listed as "Needs a choice — restore it on its own". The
rest are summarised with their address changes before confirming, and the result lists the
restored entries and the failed ones with reasons (FR-019).

## 5. Permanent delete and Empty trash (Administrators)

- **Single or selected entries**: "Delete permanently? This cannot be undone." The media
  dialog adds "The file will stop existing in storage."
- **Empty trash**:
  - the admin loads the counts per kind and remembers `trashedUpTo` as the time it loaded
    them;
  - the dialog says "{total} entries will be deleted permanently. Type {total} to confirm."
    and the button stays disabled until the typed number matches;
  - it calls Pages, Cms and Media in turn;
  - on `trash.count_changed`, it shows the new count and asks again;
  - the result is reported per kind.

## 6. Open editors (TR-16)

When a heartbeat or save of an open page, component or item answers not-found, the admin
calls `GET …/trash/{kind}/{id}`:
- **In the trash**: the banner says "{Name} moved this page to the trash at {time}". The
  editor stays read-only with copyable blocks, and "Open trash" appears when the member may
  restore.
- **Not in the trash** (404): the existing "This page was deleted" state.

## 7. Elsewhere

- **Media usage panel**: a separate "Used by content in the trash" group, which never
  triggers the in-use warning (FR-027).
- **Menu entry targets** (024): a trashed target shows as "deleted" with "(in trash)".
- **Collection delete dialog** (014): the count includes trashed items: "{n} items,
  including {t} in the trash, will be deleted permanently" (FR-023).
