# Quickstart: Border Styles for All Blocks

**Feature**: 066 | **Spec**: [spec.md](./spec.md) | **Contract**: [contracts/block-border.md](./contracts/block-border.md)

How to show the feature works, story by story. The automated suites are named in
[research.md BB-12 and BB-13](./research.md). Steps marked **by hand** need a person in a
browser.

## Prerequisites

- `./qb.ps1 start` with a fresh database, one site, a Designer account and an Editor account.
- The site's design tokens never saved (so the default radii apply), and published once.
- A page with one Section holding a Container, and inside it a Heading, a Text, an Image, a
  Button and a Divider.
- A shared component "Footer" placed on the page, and a collection with three published items
  shown by a Collection list on the page.

## 1. Automated checks

```powershell
./qb.ps1 test          # .NET (unit, architecture, integration) and admin lint, types, tests
cd tests/e2e; npm test -- block-borders.spec.ts
```

Expect:
- the contract tests (`PageSchemaContractTests`, `DesignTokenContractTests`), the token CSS
  parity tests and the golden rendering tests to pass with the new fixtures;
- the regenerated existing golden files to differ only by the five `--qb-radius-*`
  properties and the stylesheet's hashed name (BB-12, SC-004);
- the e2e spec to pass, with no axe violations (SC-006).

## 2. Frame a block (US1, FR-001 to FR-011, SC-001, SC-002)

1. As the Editor, select the Container. Expect a "Border" group after the block's own groups
   and before "Layer", with Width, Style, Colour, Corner radius and **no** Inner spacing.
2. Set Width "Thin", Style "Dashed", Colour "Brand". Expect a thin dashed brand frame on the
   canvas at once, and the block no wider than before. Time it: under 30 seconds (SC-001).
3. Select each other block type in turn. Expect the same group, with Inner spacing, on every
   one, the Section excepted (SC-002).
4. Undo. Expect the Container's frame gone. Redo, and it is back.
5. Set Width back to "None". Expect no frame, and Style and Colour disabled but kept. Set
   "Thin" again, and expect dashed brand again.
6. Save, reload the editor, publish, and open the page as a visitor. Expect the same frame
   (SC-003).

## 3. Blocks that need care (FR-006, clarifications Q2 and Q3)

1. **Text**: give it a Thin border, then Inner spacing "Small". Expect the line to move off
   the words.
2. **Button**: with style "Outline", give it Thick, colour "Text". Expect one thick text-colour
   frame, not two. Clear the border and expect the outline button as before. Give a
   "Primary" button only a radius "Full", and expect a pill.
3. **Image**: set radius "Large" with no border. Expect the photo itself rounded, on the canvas
   and published.
4. **Divider**: give it a Thin border. Expect a frame around the line, and the line unchanged.
5. **Component reference**: border the Footer on this page. Open another page using Footer and
   expect no frame there. Open the component itself and expect no frame.
6. **Collection list card**: border the card. Expect all three cards framed on the published
   page.
7. **Navigation** (**by hand**): border a Navigation block with a drop-down menu and radius
   "Large". Expect the drop-down panel to open fully outside the bar, not clipped.

## 4. Round corners (US2, FR-005)

1. Give the Container a background colour, a Thin border and radius "Medium". Expect the
   background and the line to follow the same corners, and a full-width Image inside to be
   clipped to them.
2. Set radius "Full" on a square Spacer with a border. Expect a circle.

## 5. Radius tokens (US3, FR-013 to FR-017)

1. As the Designer, open Design. Expect a "Corner radius" list with None, Small, Medium, Large
   and Full.
2. Change Medium from 0.5 to 1.5 and save. Expect every block using Medium to update on the
   canvas. Expect the published page unchanged until the design is published, and updated
   after (SC-005).
3. Delete Medium. Expect a warning with the number of blocks using it. Confirm and save. Expect
   those blocks to show their own corners (square, a Button its usual 6 px), and the drafts
   still to save.
4. As the Editor, open Design. Expect the radius list read-only. Send a token save with the
   Editor's session (for example with the browser's network tools, **by hand**). Expect 403.

## 6. Invalid values (FR-019)

Send a draft save whose Container has `"border": { "width": "huge" }`, and another whose
Section has `"border": { "padding": { "token": "space-md" } }`. Expect `props_invalid` at
`…/border/width` and `…/border/padding`. Expect the editor to refuse the same documents when
they are pasted in.

## 7. Export, import, upgrade (US4, FR-022, FR-023)

1. Export the site, import it into a fresh installation, publish, and compare the pages.
   Expect identical borders and radii.
2. Import an archive exported before this feature (`tests/fixtures/archives/` holds them). Expect
   no errors and unchanged pages.
3. Start an installation from the previous release with content, upgrade the image, and open a
   few published pages. Expect them visually unchanged. **By hand**: keep an admin tab open
   across the upgrade, then edit a page another tab bordered. Expect a refusal, not a silent
   loss (BB-11).

## 8. Accessibility (FR-012, SC-006) — **by hand**

With only the keyboard:
1. Tab into the Border group.
2. Change every control.
3. Hear each label and value read by a screen reader (NVDA or VoiceOver).
