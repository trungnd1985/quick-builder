# Feature Specification: Richer Rich Text Formats

**Feature Branch**: `035-rich-text-formats`

**Created**: 2026-10-08

**Status**: Draft

**Issue**: #35

**Input**: User description: "#35 Richer rich text formats. Problem / goal: Rich text only allows bold, italic, lists and links. That is too little for real content, such as articles with sections, quotes, images and tables. Editors need richer formatting in rich text on pages and in collection items, without giving up structured storage: rich text stays JSON, never raw HTML. In: headings (levels 2 to 4) and block quotes; underline, strikethrough, inline code, and undo and redo buttons; images from the media library, with alt text, counted in media usage; simple tables; text alignment; all of the above in the shared rich text contract, so the page editor's Rich Text block, item rich text fields, both validators and the renderer support them; converting pasted content to the allowed formats; an upgrade path for existing rich text; accessible toolbar and keyboard shortcuts. Out: raw HTML or custom embeds; HTML editors such as TinyMCE or CKEditor (they store HTML, against Principle IX, and are GPL-licensed); colours and fonts per selection; merged table cells; collaborative editing. Depends on: #11 #12 #13 #15"

## Context

Rich text appears in two places today:

- the page editor's **Rich Text block** (built in feature 011 and rendered by 012);
- **rich text fields** of collection items (014 and 015).

Both use one shared definition of what rich text may contain. Today that is paragraphs,
bulleted and numbered lists, line breaks, bold, italic and links. Nothing else can be typed,
pasted or stored, and the site renders only those formats.

That is enough for a short blurb, not for an article. Editors cannot:

- break a long text into sections;
- quote someone;
- place an image between paragraphs;
- show a small comparison in a table.

They work around it by splitting one text into many blocks, which collection items cannot do
at all.

This feature widens the shared definition. Every place that uses rich text gains the new
formats at once: the toolbar in the page editor and in the item form, the checks that refuse
invalid content, and the published page. Rich text stays structured content, never markup
(Principle IX). The new formats are additions only, so content saved before this feature
stays valid and looks exactly as it did.

## Clarifications

### Session 2026-10-08

- Q: Should a collection's rich text field be able to limit which formats it allows? → A: Yes.
  Rich text fields gain an optional setting of allowed format groups (headings and quotes;
  images; tables). All groups are allowed by default, and the server enforces the setting.
  Rich Text blocks on pages always allow every format.
- Q: When an Editor pastes content that contains images, what should happen to those images?
  → A: Images pasted as files (screenshots, copied image files) are uploaded to the media
  library through the usual upload checks and inserted without alternative text, so
  publishing asks for it. Images that exist only as web addresses are left out, with a notice.
- Q: How should the rich text toolbar show its controls, now that it grows from 5 buttons to
  about 18? → A: Icon buttons with their name and shortcut as a tooltip, in groups (history,
  block style, inline styles, lists, insert, alignment). The block style (paragraph,
  heading 2–4) is one dropdown.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Structure a text with headings and quotes (Priority: P1)

An Editor is writing an article in a Rich Text block or in a collection item's rich text
field. They mark some lines as section headings, at three levels, and some paragraphs as a
quote. The published page shows them as headings and a quote, in the site's heading style.

**Why this priority**: Sections and quotes are the most common need for longer content, and
the smallest change. They turn a wall of text into a readable article. This alone is a
useful release.

**Independent Test**: Format a text with a level 2, 3 and 4 heading and a quote, then save
and publish it. Check that the editor and the published page show each one as such, and
that the page keeps one top-level heading.

**Acceptance Scenarios**:

1. **Given** a paragraph in a rich text, **When** the Editor chooses "Heading 2" from the
   toolbar, **Then** the paragraph becomes a level 2 heading, and choosing "Paragraph" turns
   it back.
2. **Given** a published page whose rich text holds headings of levels 2, 3 and 4, **When** a
   visitor opens the page, **Then** they appear as headings of those levels, below the page's
   own top-level heading, in the site's heading font.
3. **Given** two paragraphs, **When** the Editor makes them a quote, **Then** both sit in one
   quote, which can also hold lists. Removing the quote turns them back into paragraphs.
4. **Given** an item rich text field, **When** the Editor uses headings and quotes and saves
   a draft, **Then** reopening the item shows them unchanged.

---

### User Story 2 - Inline styles, undo and keyboard shortcuts (Priority: P2)

An Editor underlines or strikes through words and marks a technical term as inline code.
They undo and redo changes with toolbar buttons or the usual keyboard shortcuts.

**Why this priority**: These are small, often requested formats. Undo and redo buttons matter
most in the item form, which has no other undo for rich text.

**Independent Test**: In a page and in an item, apply each inline style with the toolbar and
with its shortcut, undo and redo several steps, publish, and check the result.

**Acceptance Scenarios**:

1. **Given** selected words, **When** the Editor applies underline, strikethrough or inline
   code with the toolbar or its shortcut, **Then** the words show the style and the toolbar
   shows it as active. Applying it again removes it.
2. **Given** a series of changes in an item rich text field, **When** the Editor uses Undo
   and Redo, by button or by shortcut, **Then** the changes are undone and redone in order.
   Each button is unavailable when there is nothing to undo or redo.
3. **Given** the page editor, **When** the Editor uses Undo in a Rich Text block, **Then** it
   undoes the same history as the rest of the page editor, as it does today.
4. **Given** a published page, **When** a visitor opens it, **Then** underlined, struck
   through and code text show in those styles. Screen readers announce struck-through text
   as deleted text.

---

### User Story 3 - Images from the media library (Priority: P2)

An Editor places an image between paragraphs, picked from the site's media library. They
give it alternative text or mark it as decorative. The media library counts the image as
used by that page or item.

**Why this priority**: Articles need pictures in the flow of the text, and collection items
have no other way to place an image inside their text.

**Independent Test**: Insert an image in a page and in an item, set its alternative text, and
publish both. Check that the published page shows the image with that text, and that the
media library lists both usages.

**Acceptance Scenarios**:

1. **Given** the cursor between two paragraphs, **When** the Editor chooses "Image" and picks
   a file from the media library, **Then** the image appears there. Its alternative text
   starts as the file's own alternative text from the library and can be changed.
2. **Given** an image with neither alternative text nor "decorative" set, **When** the Editor
   publishes the page or item, **Then** publishing is refused and points to the image. A
   draft can still be saved. (The Image block has no such check today; this feature does not
   add one to it.)
3. **Given** a published page whose rich text shows an image, **When** someone looks at that
   file in the media library, **Then** the library lists the page as a usage, counted as live
   use, the same as an Image block. The same holds for items.
4. **Given** an image whose file was later deleted from the library, **When** the page is
   rendered, **Then** the image is left out, as for the Image block. The editor shows it as
   missing, so it can be replaced or removed.
5. **Given** a published page, **When** a visitor opens it, **Then** the image is served at a
   size that fits the text's width, with its alternative text.

---

### User Story 4 - Paste from other sources (Priority: P2)

An Editor pastes text from a word processor, an online document or a web page. Headings,
quotes, lists, tables and the supported inline styles are kept. Everything else becomes plain
text in the nearest allowed format.

**Why this priority**: Much content is written elsewhere first. Without good paste, every new
format has to be applied again by hand.

**Independent Test**: Paste a set of sample documents, each with every supported format plus
some unsupported ones, and compare the stored result with the expected one.

**Acceptance Scenarios**:

1. **Given** copied text with headings of levels 1 to 6, **When** it is pasted, **Then**
   levels 1 and 2 become level 2, level 3 stays level 3, and levels 4 to 6 become level 4.
2. **Given** copied text with colours, fonts, font sizes, highlights or embedded scripts,
   **When** it is pasted, **Then** only the text and the supported formats are kept.
3. **Given** a screenshot or a copied image file, **When** it is pasted, **Then** it is
   uploaded to the site's media library through the usual upload checks and inserted at the
   cursor, without alternative text, so publishing asks for it.
4. **Given** copied web content with images that exist only as web addresses, **When** it is
   pasted, **Then** those images are left out. The Editor is told how many were left out, and
   that images can be added from the media library.
5. **Given** a copied table with merged cells, **When** it is pasted, **Then** it becomes a
   simple table. Each merged cell's text sits in its first cell, and the others are empty.

---

### User Story 5 - Simple tables (Priority: P3)

An Editor inserts a table, adds and removes rows and columns, types in the cells, and marks
the first row as a header row.

**Why this priority**: Tables are useful for comparisons and schedules. But they are needed
less often, and they are the largest change to edit and render well.

**Independent Test**: Build a 3 by 4 table with a header row in a page and in an item. Add
and remove a row and a column, then publish. Check the result by keyboard and with a screen
reader.

**Acceptance Scenarios**:

1. **Given** the cursor in a paragraph, **When** the Editor inserts a table, **Then** a table
   of 3 rows and 3 columns with a header row appears. Tab and Shift+Tab move between cells.
2. **Given** a table, **When** the Editor adds or removes a row or a column, or turns the
   header row on or off, **Then** the table changes to match, within the table limits.
3. **Given** a published page with a table, **When** a visitor uses a screen reader, **Then**
   header cells are announced as the headers of their columns. On a narrow screen, the table
   scrolls sideways inside its area instead of widening the page.

---

### User Story 6 - Align text (Priority: P3)

An Editor aligns a paragraph or a heading to the left, centre or right.

**Why this priority**: It's cosmetic, but often requested for headings and short lines.

**Independent Test**: Align paragraphs and headings in a page and an item, and publish them.
Check the result, including that existing text keeps its alignment.

**Acceptance Scenarios**:

1. **Given** a paragraph, **When** the Editor chooses "Align centre", **Then** that paragraph
   is centred in the editor and on the published page. Other paragraphs are unchanged.
2. **Given** a Rich Text block with a block-wide alignment set, **When** a paragraph has no
   alignment of its own, **Then** it follows the block's alignment. A paragraph's own
   alignment takes precedence.

---

### Edge Cases

- **Content from before this feature**: every stored rich text stays valid without
  conversion, and its published page renders exactly as before.
- **Limits**:
  - Tables and images count toward the existing limit of 20,000 characters of text per rich
    text.
  - A table holds at most 20 columns and 200 rows.
  - A rich text holds at most 50 images and 20 tables.

  Beyond a limit, the action is refused with a message that names the limit.
- **Empty structures**: an empty quote, or a table whose cells are all empty, is kept while
  editing. A rich text is **empty** when it holds no text and no images: empty paragraphs,
  headings, quotes and tables don't count. An empty rich text is no value, so a required
  field reports it as missing. A rich text holding only an image is not empty.
- **Nesting**:
  - Headings and tables cannot sit inside a list or a table cell.
  - Quotes can hold paragraphs and lists, but not other quotes, tables or headings.

  Pasted content that breaks these rules is flattened to the nearest allowed form.
- **Images in a table cell or a list**: not allowed. An image always sits between blocks.
- **Schema changes on collections (014)**: converting a rich text field to a text field:
  - keeps the text of headings and quotes as lines;
  - joins table cells with spaces, row by row;
  - drops images, without adding their alternative text.

  The preview's examples and counts reflect this.
- **Media deleted or replaced**: replacing a file updates every rich text that shows it.
  Deleting a file in use warns and lists the pages and items, as for Image blocks.
- **Another site's media**: an image may only show a file of the same site. An image that
  names another site's file is treated exactly like a missing file: it is never shown on the
  published page, the editor shows it as missing, and an item reports it as missing media.
  The file never counts as used.
- **Links in headings, quotes and table cells**: allowed, under the existing link rules.
- **An admin tab opened before the upgrade and still open**: saving content from it still
  works, but it can't show the new formats until it is reloaded.

## Requirements *(mandatory)*

### Functional Requirements

**The shared rich text definition**

- **FR-001**: The shared rich text definition MUST add the following, without removing or
  changing anything that exists:
  - headings of levels 2, 3 and 4;
  - quotes, holding paragraphs and lists;
  - images that name a media file of the same site, with alternative text or a decorative
    flag;
  - tables made of rows and cells, where each cell holds paragraphs and the first row may be
    a header row;
  - the inline styles underline, strikethrough and inline code;
  - an alignment (left, centre or right) on paragraphs and headings.
- **FR-002**: Rich text MUST remain structured content. No stored rich text may hold markup,
  styles, scripts or any format outside the definition, whatever was typed, pasted or sent.
- **FR-003**: The definition MUST be one shared document. The page editor, the item form, the
  server's checks, the admin's checks and the renderer MUST all use it, together with example
  documents that every check judges the same way.
- **FR-004**: Every rich text that was valid before this feature MUST stay valid and MUST
  render exactly as before. No stored content is rewritten.
- **FR-005**: The limits in the edge cases MUST be checked on save. A failed check MUST give a
  problem that identifies the limit and the place in the text: a named problem for the
  image, table and character counts and the table shape, and the exceeded size (its maximum,
  for example 200 rows) for the row and column limits. The editor MUST refuse an action that
  would go past a limit, with a message that names it.

**Editing**

- **FR-006**: The rich text toolbar MUST offer every format, both in the page editor's Rich
  Text block and in item rich text fields:
  - a block style choice: paragraph, heading 2, heading 3 or heading 4;
  - quote;
  - bold, italic, underline, strikethrough and inline code;
  - bulleted list and numbered list;
  - link, image and table;
  - alignment;
  - undo and redo.
- **FR-007**: Each toolbar control MUST show whether its format is active at the cursor, MUST
  work by keyboard alone, and MUST have an accessible name. The toolbar MUST follow the
  toolbar keyboard pattern: one tab stop, with arrow keys moving between controls.
- **FR-008**: The editor MUST support these shortcuts:
  - bold, italic and underline: Ctrl/Cmd+B, Ctrl/Cmd+I and Ctrl/Cmd+U;
  - strikethrough: Ctrl/Cmd+Shift+X;
  - inline code: Ctrl/Cmd+E;
  - headings 2 to 4 and paragraph: Ctrl/Cmd+Alt+2, 3, 4 and 0;
  - undo: Ctrl/Cmd+Z;
  - redo: Ctrl/Cmd+Shift+Z and Ctrl+Y.

  Each toolbar control's description MUST list its shortcut.
- **FR-009**: In the item form, undo and redo MUST act on the rich text field being edited.
  In the page editor, they MUST keep using the page editor's single history, as today.
- **FR-010**: Choosing "Image" MUST open the media library's picker, limited to images. The
  inserted image's alternative text MUST start as the file's alternative text in the library.
  The Editor MUST be able to change it or mark the image as decorative.
- **FR-011**: Tables MUST be editable by keyboard:
  - Tab and Shift+Tab move between cells;
  - Tab in the last cell adds a row;
  - commands add and remove rows and columns, and turn the header row on and off.

**Pasting**

- **FR-012**: Pasted content MUST be converted to fit the definition:
  - headings are mapped as in User Story 4;
  - unsupported blocks become paragraphs;
  - unsupported nesting is flattened;
  - unsupported inline formats are dropped;
  - merged table cells are split;
  - images pasted as files are uploaded to the media library and inserted (FR-026);
  - images that exist only as web addresses are left out, with a notice giving their number.
    The server never fetches a pasted address.
- **FR-013**: Paste conversion MUST be checked against a set of sample documents with known
  expected results. The set MUST cover at least a word processor, an online document editor
  and a web page.

**Publishing, rendering and media**

- **FR-014**: Publishing a page or an item MUST be refused while one of its rich text images
  has neither alternative text nor the decorative flag. The refusal MUST name the image's
  place. Drafts can still be saved.
- **FR-015**: The renderer MUST produce only allow-listed output for every new format:
  - headings as headings of the stored level;
  - quotes as quotations;
  - underline, strikethrough (as deleted text) and inline code as their semantic elements;
  - tables with header cells scoped to their columns;
  - images as responsive images sized to the text's width, with their alternative text, or
    with empty alternative text when decorative.
- **FR-016**: Alignment MUST be rendered through a fixed set of classes, never inline styles.
  A Rich Text block's own alignment remains the default for paragraphs without one.
- **FR-017**: A rich text image whose file no longer exists MUST be left out of the published
  page, and shown as missing in the editor.
- **FR-018**: Media usage MUST count images in the rich text of pages, shared components and
  items, in drafts and in published data. The "in live use" rule MUST be the same as for
  Image blocks and image fields. Deleting a file in use MUST warn and list where it is used.
- **FR-019**: Converting a rich text field to a text field (014) MUST follow the rules in the
  edge cases. The preview MUST report the change as lossy whenever any of the new formats is
  dropped.

**Compatibility**

- **FR-020**: An admin page opened before the upgrade MUST NOT silently remove formats it
  does not know. When it saves content that holds the new formats, the save MUST be refused
  with a message asking the user to reload.
- **FR-021**: No log entry may contain rich text content, alternative text or file names, as
  today.

**Allowed formats per rich text field**

- **FR-022**: A collection's rich text field MUST have an optional setting of allowed format
  groups: headings and quotes, images, and tables. All three are allowed by default, so
  existing fields keep every format. The core formats (paragraphs, lists, line breaks, the
  inline styles, links and alignment) are always allowed. Rich Text blocks on pages always
  allow every format.
- **FR-023**: The item form's toolbar MUST offer only the field's allowed formats. Paste MUST
  convert content from a disallowed group: headings and quotes become paragraphs, tables
  become paragraphs of their cell text row by row, and images are left out with a notice and
  not uploaded.
- **FR-024**: The server MUST refuse to publish an item whose rich text holds a format the
  field does not allow, naming the field and the place in the text. As for other publish
  checks (015 clarification Q2), a draft with such a format may be saved; the item then needs
  attention.
- **FR-025**: Narrowing a field's allowed formats while items use them MUST be a reported
  schema change (014): existing content is kept, the affected items need attention (015), and
  publishing them is refused until the disallowed formats are removed. The change preview
  MUST count the affected items.

**Pasted image files**

- **FR-026**: An image pasted or dropped as a file MUST go through the media library's usual
  upload checks (type, size, permission to upload) and land in the library's top folder. It
  is inserted without alternative text, so publishing asks for it (FR-014). While it uploads,
  the editor shows a placeholder in its place. If the upload is refused or fails, the
  placeholder is removed and the Editor is told why; the rest of the paste is kept. If the
  member may not upload, or the field does not allow images (FR-023), the image is left out
  with a notice.

**Toolbar layout**

- **FR-027**: The toolbar MUST show its controls as icon buttons, each with its name and
  shortcut as a tooltip on hover and on keyboard focus, in this order of groups: history
  (undo, redo); block style, as one dropdown of paragraph and headings 2 to 4; inline styles;
  lists and quote; insert (link, image, table); alignment. Groups are visually separated, and
  the toolbar wraps by whole groups when narrow. Table commands (rows, columns, header row)
  appear only while the cursor is in a table. Controls the field does not allow (FR-022) are
  not shown.

### Key Entities

- **Rich text document**: structured content made of blocks (paragraphs, headings, quotes,
  lists, images and tables) and inline text with styles and links. Rich Text blocks and item
  rich text fields use it, with one shared definition and the existing size limit.
- **Rich text image**: a reference to a media file of the same site, with alternative text or
  a decorative flag. It counts as a usage of that file.
- **Table**: rows of cells holding paragraphs, with an optional header row and no merged
  cells.
- **Allowed formats setting**: an optional setting on a collection's rich text field that
  lists the allowed format groups (headings and quotes, images, tables). Missing means all.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All rich text stored before the upgrade (100%) passes the checks after it and
  renders byte-for-byte as before. A snapshot of existing pages and items shows this.
- **SC-002**: On the first try, an Editor can format a 500-word article in under 5 minutes,
  in a page or in an item. The article has three headings, a quote, an image with
  alternative text and a 3 by 3 table.
- **SC-003**: Every toolbar format can be applied and removed by keyboard alone. The editor
  and the published output pass automated accessibility checks with no violations.
- **SC-004**: For every sample in the paste set, the stored result matches the expected one,
  and none holds a format outside the definition.
- **SC-005**: A set of hostile inputs covers markup, scripts, styles, foreign attributes,
  out-of-range values and other sites' media. All of them (100%) are refused or reduced to
  the definition, and the published output contains only allow-listed elements and
  attributes.
- **SC-006**: In a rich text at the limits (20,000 characters, 50 images, and a table of 20
  columns by 200 rows), typed characters appear within 100 ms, and the page renders within
  the existing render time targets.
- **SC-007**: The server's checks and the admin's checks give the same verdict on all of the
  example documents (100%).

## Assumptions

- **No new definition version**: the new formats are additions to the existing rich text
  definition. The page schema contract's versioning rules allow that within version 1. No
  stored content is migrated, and FR-020 covers admin pages opened before the upgrade.
- **Alignment names**: alignment is stored as start, centre or end, the vocabulary blocks
  already use, and shown to editors as left, centre and right.
- **Heading levels 2 to 4 only**: the page title or a Heading block is the page's top-level
  heading, so rich text never adds a level 1 heading. Levels 5 and 6 are rarely needed.
- **Defaults chosen for this release**:
  - no code blocks, only inline code;
  - no captions or links on rich text images;
  - no image size or float choice: images take the text's width or their natural size,
    whichever is smaller;
  - no table captions, column widths or cell alignment;
  - no justified alignment, which is harder to read.
- **Limits**: 20 columns, 200 rows, 50 images and 20 tables per rich text. They add to the
  existing limit of 20,000 characters, which counts table cell text.
- **Item rich text on sites**: item fields gain the formats now. They appear on sites when
  #16 renders items, following this feature's renderer rules.
- **Same editor technology**: the formats are built on the editor that rich text already
  uses. HTML editors were considered and rejected, for Principle IX and for their licences,
  as the backlog records.
- **Permissions**: unchanged. Whoever can edit a page can use every format, and whoever can
  edit an item can use every format its field allows (FR-022). Administrators and Designers
  set the allowed formats, as they set other field settings.
- **Out of scope**:
  - raw HTML and custom embeds;
  - colours and fonts per selection;
  - merged table cells;
  - code blocks;
  - collaborative editing;
  - content search inside rich text.

## Dependencies

- **#11 editor core** and **#12 publishing and rendering**: the Rich Text block, its toolbar,
  the editor's history and the renderer's allow-list.
- **#13 media library**: the image picker, alternative text, responsive image sizes and usage
  tracking.
- **#14 and #15 collections and items**: rich text fields and their settings, the schema
  builder, the item form, schema change conversions, "needs attention" and item publishing
  checks.
