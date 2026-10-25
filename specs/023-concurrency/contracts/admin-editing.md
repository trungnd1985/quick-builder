# Admin Behaviour: Editing Sessions, Locks and Conflicts

How the editor (011) and the item form (015) use the editing API
([editing-api.yaml](./editing-api.yaml)). Decisions: [research.md](../research.md).

## 1. `useEditingSession(kind, documentId)`

A composable in `src/admin/src/editing/`, used by `EditorView.vue` (pages and components)
and `ItemFormView.vue` (items).

| Moment | Action |
|---|---|
| Document loaded | `POST editing-sessions` → presence. |
| Every 10 s, and at once when the tab becomes visible | `POST …/heartbeat { active }`. `active` is true when a change or editing action happened since the last one. |
| First change, as a viewer with access `edit` and no holder | `POST …/claim`. Saving is held until it answers (CE-04). |
| Claim refused (`editing.lock_held`) | The pending change is reverted without an undo entry, and the view becomes read-only (`locked`). |
| Router leave | `DELETE …/{sessionId}`. |
| `pagehide` | The same `DELETE` with `fetch(…, { keepalive: true })` and the usual request header. |
| Sign-out | Release every open session, then sign out. |
| `404 editing.session_gone` on heartbeat | Open a new session; if the document is gone, show 011's `gone` state. |
| `401` on heartbeat | Stop heartbeats; the existing signed-out handling applies. |

Every save sends `X-QB-Editing-Session`.

## 2. Read-only reasons (extends 011 `ReadOnlyReason`)

| Reason | Shown when | Actions |
|---|---|---|
| `locked` | Someone else holds an active lock. | None, except copying content. |
| `locked-stale` | Someone else holds a stale lock. | **Take over** (asks for confirmation). |
| `locked-self` | Another session of the same member holds it. | **Edit here instead**. |
| `taken-over` | `lostLock.reason = takeover`, or a save refused with `editing.lock_held`. | **Reload**; **Save as a copy** where allowed. |
| `moved` | `lostLock.reason = moved`. | **Reload**. |
| `view-only` | `access = view-only` (no edit capability). | None. |
| `free` | A held document became free while this session was read-only. | **Edit**: reload if the version changed, then claim (CE-14). |
| `conflict` | Unchanged from 011, now reached through the conflict dialog. | As in §3. |

## 3. Conflict dialog

`ConflictDialog.vue` (`role="alertdialog"`) replaces the conflict branch of
`ConflictBanner.vue`. Its content:
- "Nothing was overwritten. <name> changed this <kind> at <time>." For a system change:
  "The collection's fields were updated at <time>."
- For items, the fields that differ, by label (CE-10).
- **Reload latest**: confirms that unsaved changes not copied or saved as a copy will be
  discarded, then reloads and resumes autosave.
- **Keep viewing my version**: closes the dialog into the `conflict` read-only banner
  (011 FR-028).
- **Save my version as a copy**: shown only when `permissions` say the member may create
  that kind. It calls the copy endpoint, then shows a link to the copy and offers Reload.
  When it is not shown, the dialog says: "You can copy blocks from your version, or ask a
  Designer to save it as a copy." (FR-020a)

## 4. Presence banner and markers

- **`PresenceBanner.vue`** sits above the canvas or form. For a holder elsewhere it says
  "<name> is editing — since <time>", adding "inactive for <n> min" when stale. As holder it
  says "<names> also viewing". Changes are announced in a polite live region (CE-19).
- **`LockMarker.vue`** appears in the page tree, the component list and the item list, fed
  by `GET …/editing?kind=` on load and every 15 s while the list is visible (CE-15).
- **Publish and delete warnings**: the publish menu and the delete dialogs show "<name> is
  editing this; unsaved changes will not be included" (or "…will be stopped" for delete)
  when someone else holds the lock (CE-17).

## 5. Crash recovery (011 FR-030)

`RecoveryDialog` offers Restore only when the draft version is unchanged **and** a claim
succeeds. Otherwise it opens the recovered copy read-only, with "Save as a copy" where
allowed (CE-18).
