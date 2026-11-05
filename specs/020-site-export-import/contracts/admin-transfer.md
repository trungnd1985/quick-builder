# Contract: Admin Behaviour for Export and Import

**Feature**: 020 · **Requirements**: FR-006 … FR-030, FR-039 · **Research**: ST-07, ST-22

Shown only to Administrators. The server enforces every action (FR-035).

## 1. Site settings → "Export and import"

| Element | Behaviour |
|---|---|
| **Export site** button | `startExport`. Disabled while the site has an active transfer, with the reason shown. |
| Running transfer | `TransferProgress`: kind, step, percent, started by and at, **Cancel**. It polls every 2 s and resumes when the tab is reopened. |
| Finished export | summary counts, size, problems, **Download** (expires date shown), **Delete archive** |
| **Import into this site** | upload, then check (`merge`), then `ImportCheckView`, then policy choice (replace, skip, rename, with a sentence each), then confirm |
| **Restore from archive** | shown after an upload whose check says `sameSiteAsTarget`. Opens `RestoreConfirmDialog`: removed, returned and changed counts with expandable lists, collection field changes, a note that a copy of the current site is exported first, and a text field "Type the site name to confirm". |
| Recent transfers | the last 90 days: kind, mode, who, when, outcome, a link to the report |

## 2. Sites list → "Import site"

Upload, then check as `new_site`, then `ImportCheckView`. The options are:
- the site name (required, defaults to the archive's site name with " (imported)");
- "Start fresh: import content as unpublished drafts, without history";
- "Accept warnings" when the check has any.

Then confirm, progress and report, with a link to the new site.

## 3. `ImportCheckView`

Sections, in order, each with a heading. Lists show at most 1,000 entries each, with paging.
1. **Archive**: source site, release, created at, and the "same site as this one" note.
2. **Versions**: what will be upgraded.
3. **Problems**: errors first. Errors block the import; each states what is wrong and where.
4. **What will change**:
   - new site: counts, and host names that will not be added;
   - merge: conflicts by kind, each with what the chosen policy will do;
   - restore: removals, returns and changes.
5. **Past schedules** that will be dropped.

## 4. `SiteImportBanner`

Mounted in the site layout. While `site.transfer.state` is `confirmed` or `running` for an
import:
- every screen of the site shows "This site is being imported into. Changes are paused until
  it finishes (started by …, at …).";
- the editor and item form switch to read-only and stop autosaving;
- a write refused with `site.import_in_progress` shows the same message.

When the import finishes, the banner offers a reload.

## 5. Accessibility (FR-039)

- Progress is announced politely: step changes and every 10 percent.
- Completion and failure are announced assertively.
- The dialogs trap focus and return it on close.
- The typed confirmation field has a visible label and error text.
- Every list is keyboard reachable. Colour is never the only sign of an error or warning.
