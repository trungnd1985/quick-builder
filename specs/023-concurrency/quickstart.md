# Quickstart & Validation Guide

**Feature**: `023-concurrency` | **Date**: 2026-10-25

How to check, end to end, that two members never overwrite each other, see who else is
editing, recover from idle or vanished holders, and keep their version as a copy. This guide
is for validation only. Details are in:
- tables, states, rules and errors: [data-model.md](./data-model.md);
- endpoints: [contracts/editing-api.yaml](./contracts/editing-api.yaml);
- admin behaviour: [contracts/admin-editing.md](./contracts/admin-editing.md);
- interfaces, edges and events: [contracts/module-surface.md](./contracts/module-surface.md);
- decisions, numbered `CE-xx`: [research.md](./research.md).

Record results, timings and versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation, sites, members, cookies and shorthands**: as in 024's quickstart: sites
  `$S` and `$S2`; cookies `$COOKIE` (Administrator), `$DESIGNER`, `$DESIGNER2` (a second
  Designer on `$S`), `$EDITOR`; `H` is `-H "X-QuickBuilder-Request: 1"`.
- **Content**: page "Pricing" `$P` on `$S`; the header component `$HEADER`; the "Blog posts"
  collection `$C` with a post `$POST`.
- **Two browsers** (or one normal and one private window), signed in as different members.
- **Automated suites**:

  ```bash
  dotnet test                                    # unit, architecture, integration (Docker)
  dotnet test --filter "FullyQualifiedName~Concurrency"   # LockRace, OverlappingDraftWrites (SC-001)
  cd src/admin && npm run lint && npm run type-check && npm test
  ```

---

## 1. No overwrite (User Story 1, SC-001)

1. Run `OverlappingDraftWritesTests`: 1,000 pairs of writes on pages, components and items.
   Expect zero overwrites, and every refusal names the other change.
2. API check, page:
   1. `GET` the draft of `$P` twice (as `$DESIGNER` and `$DESIGNER2`) and note `version`.
   2. Save as `$DESIGNER2`.
   3. Save as `$DESIGNER` with the old version → `409 pages.out_of_date` with `changedBy`
      naming the second Designer, `changedAtUtc` and `currentVersion`.
3. Item: the same with `$POST` → `409 cms.item_conflict` with `changeKind: member`. Then
   publish `$POST` as `$DESIGNER2` and save its draft as `$DESIGNER` with the latest
   `draftRevision`. Expect success: publishing does not refuse the draft save (CE-08).
4. Schema change: rename a field option in `$C` that `$POST` uses, then save the old form.
   Expect `changeKind: schema_change` and `changedBy: null`.
5. In the browser, produce a conflict in the editor and in the item form. Check:
   - the dialog's text and its three actions;
   - the item form lists the changed fields;
   - Reload asks for confirmation first;
   - Keep viewing leaves the version read-only, with blocks that can be copied and pasted
     after a reload.

## 2. Presence and the lock (User Story 2, SC-002, SC-003)

1. Browser A (`$DESIGNER`) opens `$P`. Browser B (`$DESIGNER2`) opens `$P`. Each shows the
   other as "also viewing" within 15 s. Both can still edit.
2. Type in A. B turns read-only within 15 s with "… is editing — since …". The page tree in
   B shows the marker.
3. Make A and B type at the same moment, many times (`LockRaceTests` covers this in
   automation). Exactly one becomes the holder; the other's change disappears and it is told
   who is editing.
4. A navigates to the page tree. B is told "now free" within 15 s. Choosing **Edit** reloads
   the draft when A saved, then lets B edit.
5. As a member of `$S2` only, call `GET /api/v1/editor/sites/$S/editing?kind=page` → `404`.
   Open `$S2`'s lists: there are no markers from `$S` (FR-013).
6. Same member, two tabs: in tab 1 type; tab 2 shows "being edited in another of your
   sessions" with **Edit here instead**. Choose it. Tab 1 turns read-only with a notice.
   No takeover row is written.
7. As `$EDITOR`, open `$HEADER`. It is view-only and never claims, and the holder is shown.

## 3. Stale locks, takeover, release and expiry (User Story 3, SC-004)

Set the clock forward with the test clock in automation (`EditingRulesTests`,
`EditingSessionEndpointsTests`). By hand:

1. A types, then stops. After 30 minutes B sees "inactive for 30 min" and **Take over**.
   Take over: B edits, and A shows "… took over at …", read-only, with Save as a copy. One
   row is added to `editor.editing_takeovers`. The log has a takeover entry with ids only.
2. Two members take over the same stale lock at once. One succeeds; the other gets
   `editing.not_stale`.
3. Fencing: with A's network off, make a change in A; take over in B; restore A's network.
   A's queued autosave is refused with `editing.lock_held`. B's draft is untouched.
4. Release: A closes the tab → B is told "now free" within 15 s. Repeat with in-app
   navigation and with sign-out.
5. Expiry: kill A's browser process. Within 2 min 15 s the lock is gone.
6. Lost permission: remove A's Designer role while A holds `$P`. Within 15 s A is told and
   the lock is released.
7. Deletion: delete `$P` while B views it. B's editor shows the `gone` state on the next
   heartbeat.
8. Restart the app while A holds the lock and both are open. Saves stay protected by version
   (FR-026). Sessions re-open on the next heartbeat (`editing.session_gone` → new session).

## 4. Save a copy (User Story 4)

1. As a Designer, produce a page conflict and choose **Save my version as a copy**. Check:
   - "Pricing (copy)" appears right after "Pricing", unpublished;
   - its slug is unique;
   - opening it shows who made it, when, and from which version;
   - publishing "Pricing" does not publish the copy.
2. Component: the same; "<name> (copy)" is used by no page.
3. As `$EDITOR`, produce an item conflict and save a copy: a new unpublished item with a
   unique slug. Remove a field from `$C` before saving the copy: the response lists the
   dropped field and the member is told.
4. As `$EDITOR`, produce a page conflict: **Save as a copy** is not offered, the dialog says
   why, and a crafted request gets `403 pages.copy_forbidden`.

## 5. Recovery (011 FR-030 with locks)

1. Hold `$P` in A with unsaved changes, then kill the browser. Before the lock expires, open
   `$P` in B and type (B cannot, because it is locked). Once it expires, B claims and saves.
2. Re-open A: the recovery dialog does not offer Restore (the draft changed). It opens the
   copy read-only, with Save as a copy.

## 6. Multiple instances (FR-025)

Run `LockRaceTests` in its two-instance mode (two app hosts, one database). With the
reference compose file scaled to two app containers behind the proxy, repeat §2.2 to §2.4.
Both browsers always agree on the holder.

## 7. Accessibility (FR-027; manual part of E-5)

1. vitest axe checks pass for `PresenceBanner`, `ConflictDialog` and `LockMarker`.
2. With a screen reader, check:
   - "also viewing", "is editing", "now free" and "took over" are announced politely;
   - the conflict dialog is announced on open, and focus moves to its first action;
   - every action works from the keyboard.

## 8. Upgrade (Principle III)

`UpgradeFrom019Tests` (T067): a database from release 0.1.0 (the last feature merged is 019)
upgrades. Every item has
`draft_revision = revision`, and existing drafts save with an old-style request (no
`draftRevision`, no session header).

## 9. No added delay (SC-007)

On the reference compose setup, measure the median of 10 cold opens (browser cache cleared)
on `main` and on this branch:
- the page tree of `$S`, with about 50 pages;
- the item list of `$C`, with about 200 items;
- the editor on `$P`, until the canvas is ready.

Take the time from the browser's network panel or the Performance timeline. The branch may
be at most 10% or 100 ms slower, whichever is larger. The editing session opens in parallel
with the draft load, so it should not add a round trip (plan, Performance Goals).

## 10. Usability check (SC-005)

With at least five team members who have not seen the feature, give each, in pairs on two
computers, these tasks without help:
1. Find out who else is editing a page, and since when.
2. After a conflict, say whether anything was overwritten, then reload the latest version.
3. Take over a page whose editor has been away for more than 30 minutes (use the test clock,
   or prepare a stale lock).
4. As a Designer, save your version as a copy after a conflict.

Record for each participant whether they answered 1 and 2 correctly, and whether they
completed 2–4 without help. SC-005 passes when every participant does.
