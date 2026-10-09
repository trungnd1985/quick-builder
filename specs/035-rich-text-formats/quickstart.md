# Quickstart: Richer Rich Text Formats

**Feature**: 035 | **Spec**: [spec.md](./spec.md) | **Contracts**: [contracts/](./contracts/)

How to show the feature works, story by story. Automated suites are named in [research.md RT-19](./research.md). Steps marked **by hand** need a person in a browser.

## Prerequisites

- `./qb.ps1 up` with a fresh database, a site, and an Editor account.
- A page with a Rich Text block, and a collection "Articles" with a rich text field "Body" and a rich text field "Summary".
- Some images in the media library: at least one with alternative text, and one without.
- `./qb.ps1 seed-items -Collection articles` for list data (optional).

## 1. Headings and quotes (US1, SC-002)

1. In the page's Rich Text block, type three paragraphs. Make the first Heading 2 with the block style menu, the second Heading 3 with Ctrl/Cmd+Alt+3, and the third a quote.
2. Save and publish, then open the page. Expect `h2`, `h3` and `blockquote` inside the block, in the site's heading font, and one `h1` on the page.
3. Repeat in an Articles item's Body. Save the draft, reload, and expect the same.

## 2. Inline styles, undo, shortcuts (US2)

1. Apply underline (Ctrl/Cmd+U), strikethrough (Ctrl/Cmd+Shift+X) and inline code (Ctrl/Cmd+E), each from the keyboard and from the toolbar. Expect the toolbar's pressed state to follow the cursor.
2. In the item Body, make five changes. Undo three with the button and redo one with Ctrl+Y. Expect the button states to match.
3. In the page editor, undo inside the block and expect the page's history (the layer tree and the block undo together).
4. Publish, then check `u`, `del` and `code` in the page's HTML.

## 3. Images (US3, FR-014, FR-018)

1. In the Body, choose Image, pick the file that has alternative text, and expect it prefilled. Pick the file without alternative text, then publish: expect a refusal naming the image. Mark it decorative, publish, and expect success.
2. Do the same on the page. Expect `pages.image_alt_missing` until it's fixed.
3. Open each file in the media library. Expect the page and the item as usages, with "in live use" after publishing.
4. Replace the file's content, and expect the published page to show the new image after re-render. Delete it (as an Administrator, confirming the warning), and expect the published page to show no image and the editor to show "Missing image".

## 4. Paste (US4, FR-012, FR-026, SC-004)

1. Copy a section of a Word or Google Docs document with headings (1–3), a list, a table with a merged cell, coloured text and an image. Paste it into the Body. Expect:
   - headings at levels 2 and 3;
   - the list kept;
   - the table split into a rectangular grid;
   - no colour;
   - the image left out, with a notice "1 image was left out".
2. Take a screenshot and paste it. Expect an upload placeholder, then the image. It has no alternative text, so publishing asks for it.
3. As an account without upload permission (if a role lacks it), paste a screenshot. Expect a notice, and no upload.
4. Automated: `paste.spec.ts` runs the full sample set.

## 5. Tables (US5)

1. Insert a table. Expect 3 × 3 with a header row. Use Tab to the last cell and Tab again: expect a new row. Add and remove a column, and turn the header row off and on.
2. Publish, open the page at a phone width, and expect sideways scrolling inside the table only. **By hand** with a screen reader: header cells are announced as column headers.

## 6. Alignment (US6)

1. Centre a heading and right-align a paragraph. Publish and expect the `qb-align--center` and `qb-align--end` classes and no `style` attribute.
2. Set the block's alignment to centre, with one paragraph left-aligned. Expect that paragraph to stay at the start.

## 7. Limits and performance (edge cases, SC-006)

1. Paste a document with 51 images (from `examples/invalid`). Expect a refusal naming the limit.
2. **By hand**: in an item with a 20 × 200 table and 20,000 characters, type in the last cell. Characters must appear without visible delay (under 100 ms; use the browser's performance panel).
3. Automated: `RichTextRenderPerformanceTests`, plus the normalisation benchmark in vitest.

## 8. Allowed formats per field (FR-022–FR-025)

1. In the Articles schema, edit Summary and uncheck "Allow headings and quotes" and "Allow tables". Expect a preview reporting how many items need attention, if any use them. Save.
2. In an item, expect Summary's toolbar to show no heading, quote or table controls. Paste a document with headings and a table into Summary, and expect paragraphs only.
3. For an item that already had a heading in Summary, expect "needs attention" in the list, and a publish refusal naming the field. Remove the heading and publish.
4. Re-check the boxes. Expect a free change with no preview, and the attention cleared.

## 9. Schema conversion (FR-019)

Change Body to a text field. Expect a lossy preview whose example shows headings as lines, table rows as lines of cell text, and no images. Cancel.

## 10. Outdated admin tab (FR-020)

1. **By hand**: open an item in a browser tab, deploy this build (or simulate it by removing the `X-QB-Rich-Text` header with the browser's request override), add a heading in another tab, then save from the first. Expect "This page of the admin is out of date…" and nothing saved.
2. Automated: `AdminOutdatedTests`.

## 11. Upgrade (SC-001)

`UpgradeFrom015Tests` loads a 015 snapshot, validates every page and item, and compares rendered HTML with the snapshot's. Expect byte-identical output.

## 12. Accessibility (SC-003)

1. Automated: axe on every toolbar state (paragraph, table, image), the item field and the published fixtures.
2. **By hand**, keyboard only: Tab to the toolbar (one stop), move with the arrow keys through every control, and apply and remove every format.
