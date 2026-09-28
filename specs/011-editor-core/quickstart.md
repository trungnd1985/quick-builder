# Quickstart & Validation Guide

**Feature**: `011-editor-core` | **Date**: 2026-09-28

How to check, end to end, that the visual editor and design tokens work, and how to verify
each success criterion. This guide covers validation only:
- block definitions, token sets and editor state are in [data-model.md](./data-model.md);
- endpoints are in [contracts/editor-api.yaml](./contracts/editor-api.yaml);
- the document format is in
  [contracts/page-schema-blocks.md](./contracts/page-schema-blocks.md) and
  [contracts/design-tokens.md](./contracts/design-tokens.md);
- decisions are numbered `ED-xx` in [research.md](./research.md).

Browser checks run in current Chrome or Edge and in Firefox, on a window at least 1,280 px
wide. Record results, timings and browser versions in `quickstart-results.md`.

---

## Prerequisites

- **Installation and site**: as in 010's quickstart: a site `$S`, a second site `$S2`, and
  cookies `$COOKIE` (Administrator), `$DESIGNER` and `$EDITOR`.
- **Pages**: at least **Home**, **About** and **Contact** on `$S`, and a component
  **Header**.
- **Media**: two images uploaded to `$S`'s media library.
- **Shorthands**: `$API=http://127.0.0.1:8080/api/v1`. `H` stands for
  `-H "X-QuickBuilder-Request: 1" -H "Content-Type: application/json"`.
- **Automated suites**:

  ```bash
  dotnet test                                   # unit, architecture, integration
  cd src/admin && npm run lint && npm run type-check && npm test
  ```

---

## 1. Build a page by dragging (US1, SC-001, SC-002)

In the admin shell, open **Pages**, then **Edit** on **About**. With a timer running:
1. Drag a **Section** onto the empty canvas. Drag a **Heading** and a **Text** into it,
   and type "About us" and a paragraph on the canvas.
2. Drag an **Image** below the text and choose an image from the media library.
3. Add a **Button** linking to the **Contact** page.
4. Add a second **Section** with a horizontal **Container** holding two **Text** blocks.
5. Drag the Image above the Heading. Duplicate the Button, then delete the copy.

**Expected**:
- **Timing**: finished in under 10 minutes, with no JSON typed (SC-001).
- **Drop rules**: a Section cannot be dropped inside a Section; the indicator shows "not
  allowed", and releasing changes nothing.
- **Saving**: the status shows *Saved* within about 3 seconds of stopping. Reload the
  editor, and the page is identical.
- **Stored draft**: it passes validation and holds the ids you saw:

  ```bash
  curl -s -b "$COOKIE" $API/pages/sites/$S/pages/$ABOUT/draft | jq '.document.root.children[].type'
  ```

- **Other sites**: `/sites/$S2/pages/$ABOUT/edit` shows "not found".

**Automated**: `ops.spec.ts`, `EditorView.spec.ts` and `documentValidator.spec.ts`.

---

## 2. Edit properties (US2)

Select each block type in turn and change every property in the panel.

**Expected**:
- **Controls**: the panel shows exactly the properties in
  [data-model §1.2](./data-model.md), with their defaults.
- **In-place text**: editing on the canvas and editing in the panel stay in step.
- **Rich text**: bold, italic, a link to a page, an external link, and both kinds of list
  work. Pasting a web page's content keeps only those formats.
- **Refusals**: the panel refuses each of these with a reason and keeps the last valid
  value:
  - a Button link to `javascript:alert(1)`;
  - an Embed of `https://example.com/video`;
  - a Heading over 300 characters.
- **Embeds**: an Embed of a YouTube address shows a placeholder, and the browser's Network
  panel shows no request to youtube.com (SC-008).

Server-side checks, which also cover a modified client:

```bash
curl -s -b "$COOKIE" $H -X PUT --data @section-in-section.json $API/pages/sites/$S/pages/$ABOUT/draft
                                        # 400 pages.invalid_document, rule nesting_not_allowed
curl -s -b "$COOKIE" $H -X PUT --data @js-link.json $API/pages/sites/$S/pages/$ABOUT/draft
                                        # 400 pages.invalid_document, rule props_invalid, pointer …/href
```

**Automated**:
- `PropertyPanel.spec.ts` and `BlockRegistryCoverage.spec.ts`;
- `PageDocumentValidatorTests`, over the golden files in
  [page-schema-blocks.md §8](./contracts/page-schema-blocks.md).

---

## 3. Undo, redo, autosave, conflicts and recovery (US3, SC-003, SC-004)

1. **Undo**: make 20 mixed changes, press `Ctrl+Z` until nothing is left, and confirm the
   page is as it started. Press `Ctrl+Shift+Z` until nothing is left, and confirm it is as
   it ended.
2. **Typing**: type a sentence and undo once. A word or a pause disappears, not one
   character.
3. **Network loss**: in DevTools, set the network to *Offline* and make a change. The
   status says *Not saved, retrying*. Go back online. The status says *Saved* without any
   action.
4. **Conflict**: open the same page in a second tab, change it there and wait for *Saved*.
   Change the first tab.
   - **Expected**: the first tab shows the conflict banner and goes read-only, with no
     overwrite option. Copy a block (`Ctrl+C`), press **Reload latest**, paste
     (`Ctrl+V`), and the block is back.
5. **Recovery**: go offline, make a change and close the tab. Go online and reopen the
   page.
   - **Expected**: the editor offers **Restore**. Accept, and the change is there.
   - Repeat, but save a change from another tab before reopening. **Expected**: the editor
     offers only **Open read-only to copy blocks**.
6. **Leaving**: with unsaved changes, click **Pages** in the navigation. A warning appears.
7. **Sign-out**: sign out and sign in as the Editor on the same browser. No recovery offer
   and no clipboard content from the previous user appear.

**Automated**: `ops.property.spec.ts` (SC-003), `autosave.spec.ts` and
`recovery.spec.ts`.

---

## 4. Layer view (US4)

Open a page with at least 30 blocks nested 4 levels deep. The generator is
`tests/fixtures/editor/deep-page.json`; save it through the draft API.

**Expected**:
- **Selection**: selecting a deep Button in the layer view selects it on the canvas and
  scrolls to it, and the reverse works too.
- **Moving by pointer**: dragging a Container to another Section in the layer view moves
  it on the canvas.
- **Moving by keyboard**: `Alt+↑`, `Alt+↓`, `Alt+→` and `Alt+←` move blocks by keyboard
  only; the live region announces each move.
- **Labels**: renaming a Section to "Hero" changes the layer view only. The canvas and the
  draft's visible text are unchanged, and `layerLabel` is stored.

**Automated**: `LayerView.spec.ts`, which includes axe and the keyboard paths.

---

## 5. Responsive preview (US5)

Switch between **Desktop**, **Tablet** and **Mobile**.

**Expected**:
- **Widths**: the canvas is 1,280, 768 and 375 px wide.
- **Stacking**: the horizontal Container stacks at mobile width. Turn off *Stack on mobile*
  and it stays horizontal.
- **Editing**: it works at every width.
- **Remembered**: reloading keeps the chosen width.

---

## 6. Design tokens (US6, SC-007)

As the Designer, open the site, then **Design**.

1. **Change a value**: set *Brand* to green. Reopen the About editor, and every block
   using Brand is green, with no block edited.
2. **Rename**: rename *Brand* to *Primary*. Blocks keep using it.
3. **Delete**: delete *Brand light*.
   - **Expected**: the warning states how many blocks use it, matching:

     ```bash
     curl -s -b "$COOKIE" $API/pages/sites/$S/token-usages/color-brand-light
     ```

   - After confirming, those blocks show their default colour, the property panel marks
     "deleted colour", and the page still saves.
4. **Last colour**: deleting the last colour is refused (`editor.token_required`).
5. **Fonts**: choose the *Humanist* font for headings. The canvas changes, and the
   Network panel shows no font download (SC-008).
6. **Editor role**: as the Editor, **Design** shows the tokens read-only.
   `PUT $API/editor/sites/$S/design-tokens` returns `403 identity.forbidden`.
7. **Defaults**: a new site reads the default set with `version: 0` and `isDefault: true`.

**Automated**:
- `DesignTokensApiTests`, `DesignTokenSetTests` and `DesignTokenContractTests`;
- `EditorAuthorizationTests` and `TokenUsageApiTests`;
- `DesignTokensView.spec.ts`.

---

## 7. Shared components (US7)

1. From **Components**, choose **Edit** on **Header**. Build a Section with an Image and
   three Buttons. The palette offers no components.
2. Open **Home**. From the palette's **Components** group, insert **Header** at the top.

**Expected**:
- **Display**: the header shows in place, marked *Shared*. It is selected as one unit, and
  its content can't be edited there.
- **Opening**: the panel offers **Open component**.
- **Placement**: the Header cannot be dropped inside a Section (`nesting_not_allowed`).
- **Editor role**: as the Editor, the component list offers no **Edit**.

---

## 8. Performance (SC-005)

Save `tests/fixtures/editor/page-500.json` (500 blocks) to a page through the draft API,
then open it in the editor on the reference environment (2 vCPU, 4 GB, compose).

**Expected**, measured with the Performance panel or the `performance.measure` marks
`qb:select`, `qb:set-prop` and `qb:drop` that the editor emits in development builds:
- the editor opens in under 3 s;
- selecting a block, changing a property and dropping a dragged block each take under
  100 ms from input to paint.

**Automated, server side only**: `EditorOpenPerformanceTests` (fetching the draft, tokens and
components) and `TokenUsagePerformanceTests`, both in the smoke project.

---

## 9. Keyboard, translation and accessibility (SC-006)

Unplug the mouse. For each of the ten block types:
1. insert it from the palette;
2. edit it;
3. move it into and out of a Container;
4. duplicate it;
5. delete it.

**Expected**: everything is reachable. `npm run lint` passes the vue-i18n rule, so there
are no raw strings, and axe finds no violations in `EditorView.spec.ts`,
`LayerView.spec.ts`, `PropertyPanel.spec.ts` or `DesignTokensView.spec.ts`.

---

## 10. Contracts, upgrade and smoke (SC-009, FR-043, FR-044)

```bash
dotnet test --filter "FullyQualifiedName~PageSchemaContractTests|FullyQualifiedName~PageSchemaSubsetTests|FullyQualifiedName~DesignTokenContractTests|FullyQualifiedName~UpgradeFrom010Tests"
cd src/admin && npx vitest run documentValidator designTokens BlockRegistryCoverage
```

**Expected**:
- **Contracts**: the contract documents and schema files agree, and every block type has at
  least one valid and one invalid golden file.
- **Upgrade**: a 010 snapshot upgrades. Its drafts are unchanged, still valid, and open in
  the editor. Its sites read the default tokens.
- **Smoke**: the release smoke test (`ReferenceDeploymentSmokeTests`) serves the editor
  route and saves and reads back a Section with a Heading. The browser half of FR-044 is §1
  of this guide until #25 (exception E-5).
