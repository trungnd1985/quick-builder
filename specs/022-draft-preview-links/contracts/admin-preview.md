# Contract: Admin Behaviour for Previews and Links

What members see and do in the admin. Research: PV-14, PV-13.

## 1. Preview

| Where | Control | Behaviour |
|---|---|---|
| Editor top bar (`EditorTopBar.vue`) | **Preview** | Flushes pending autosave, then opens `/_preview/sites/{siteId}/pages/{pageId}` in a new tab, with `?item=` for a template page and the editor's sample item. If the save is refused (023 conflict, validation), no tab opens and the save error is shown. |
| Item form (`ItemPublishBar.vue`) | **Preview** | The same for `/_preview/sites/{siteId}/items/{itemId}`. Disabled with "This collection has no template page" when there is none. |
| Page tree and item list row menus | **Preview** | Opens the saved draft directly. |
| Page history (`HistoryView.vue`), item history (`ItemHistoryPanel.vue`) | **Preview** per retained version | Opens `?version={n}`. Versions without retained content show no button. |

Every Preview control is a real link (`<a target="_blank" rel="noopener">`) where no save is
needed, so it works with the keyboard and a middle click.

## 2. Share preview (`SharePreviewDialog.vue`)

**Opened from**:
- "Share preview" next to Preview, for the draft;
- "Share" on a version in either history;
- the editor or item form address with `?share=1` (and `&version=n`), which the member
  preview bar links to.

**Shown to**: members holding the kind's edit capability. Others do not see the control,
and the server checks anyway.

**Create**:
- **Label**: optional, at most 80 characters, for example "Client review".
- **Expires**: radio buttons for 1 hour, 1 day, **7 days** (selected) and 30 days, plus
  "Custom" with a date-time picker limited to the next 30 days. The time is in the browser's
  zone and sent as UTC.
- **Create link**: after creating, the dialog shows the full link in a read-only field with
  **Copy**, and the note "This link is shown only once. Anyone with it can view this
  [draft | version N] until {expiry}."

**Active links**:
- A table with label, created by, created, expires and last opened ("never" when unopened).
- Each row has **Revoke**, with a confirmation.
- **Revoke all** has a confirmation that states the count.
- The full link is never shown again.

**At the limit (20)**: Create is disabled with "This [page | item] has 20 active links.
Revoke one to create another."

**Errors**: `preview.link_limit`, `preview.invalid_expiry`, `preview.invalid_label`,
`preview.no_template` and `preview.version_not_retained` each get a message in `en.json`.

## 3. Site preview links (`PreviewLinksView.vue`)

**Where**: Site settings → "Preview links". Shown to Administrators only, and the route
guard checks `editor.preview_links.manage`.

**Contents**:
- every active link of the site, sorted by expiry and paged;
- each row shows the target (page title or item name, with its collection), draft or
  version, label, creator, created, expires and last opened;
- each row has **Revoke**, and there is **Revoke all** for the site, with a confirmation
  stating the count.

**Empty state**: "No active preview links."

## 4. History entries

Page history and item history show "Preview link created (expires {time})" and "Preview link
revoked", with who and when, for a draft or "version N". The label and link are never
shown.

## 5. Accessibility

- Dialogs follow the admin's existing dialog pattern: focus trap, Escape closes, focus
  returns to the opener.
- The copy button announces "Link copied" in a live region.
- The axe checks in `SharePreviewDialog.spec.ts` and `PreviewLinksView.spec.ts` run as in
  other admin tests.

## 6. Preview bar, as seen by a member (rendered server-side, PV-13)

```text
┌───────────────────────────────────────────────────────────────────────────────┐
│ PREVIEW — not live · Draft saved 2026-11-19 10:42 UTC by Lan                  │
│ Header shown from its draft (never published) · Scheduled to publish 21 Nov   │
│ [Back to editor] [Share preview]                                [Hide bar]    │
└───────────────────────────────────────────────────────────────────────────────┘
```

A link viewer sees the same bar without the two admin links, and with "This link expires
2026-11-26 10:42 UTC".
