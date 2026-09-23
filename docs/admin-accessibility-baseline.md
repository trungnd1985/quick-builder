# Admin accessibility baseline

**Status**: normative for every admin screen, from this feature onwards (FR-045b).

This is deliberately short. It is the floor every later admin screen inherits, not a
complete accessibility policy — a short list that is actually met beats a long one that
is aspirational.

## The baseline

| # | Requirement | How it is checked |
|---|---|---|
| A1 | Every interactive element is reachable and operable by keyboard alone | Manual pass per screen; axe covers the structural part |
| A2 | Focus is always visible, never suppressed | `:focus-visible` is defined once in `admin/src/styles.css`; removing an outline is a review failure |
| A3 | Text meets WCAG 2.1 AA contrast: 4.5:1 body, 3:1 large text and UI boundaries | Palette tokens in `styles.css` carry their measured ratios |
| A4 | Every form control has an associated `<label>` | axe (`label` rule) |
| A5 | State is never conveyed by colour alone | Review; the active nav link carries `aria-current` as well as colour |
| A6 | Errors are tied to their input with `aria-describedby` and announced with `role="alert"` | axe + review |
| A7 | Each screen has one `<h1>` and a sensible heading order | axe (`heading-order`, `page-has-heading-one`) |
| A8 | Landmarks: `<header>`, `<nav>`, `<main>`, plus a skip link ahead of the navigation | axe (`region`, `bypass`) |

## What is deliberately not here

- **Screen reader testing.** No automated check substitutes for it, and this feature has
  two screens. Worth doing once real workflows exist.
- **Colour-blind simulation.** A5 covers the failure mode that matters today.
- **Localisation of layout (right-to-left).** Out of scope for this feature; one language
  ships. The text lookup exists so adding one later does not mean reopening screens.

## Why a baseline rather than "make it accessible"

"Accessible" is not a testable instruction, and a rule nobody can check is a rule that
decays. Eight numbered items can be cited in review, asserted in CI, and argued about
specifically.

The cost argument matters too: every item above is close to free while the shell has two
screens, and expensive once several modules have copied a pattern that lacks them. That
is the same reasoning behind putting the text lookup in from the start — see
[the spec's clarifications](../specs/001-foundation-skeleton/spec.md).

## Related

- `admin/src/styles.css` — the tokens and the focus indicator
- `admin/eslint.config.js` — the `no-raw-text` rule enforcing the text lookup (FR-045a)
- SC-013 — every admin journey completable by keyboard, zero raw strings
