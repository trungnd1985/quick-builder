// Design tokens as CSS custom properties (docs/contracts/page-schema.md §9, 011 ED-09).
//
// The canvas frame carries these; block styles only ever say var(--qb-…). Changing a token's
// value therefore changes every block that uses it without touching the blocks (FR-033).
//
// Sizes are `calc(var(--qb-base-size) * n)` rather than `n rem`: in the admin document `rem`
// is the admin's own root size, while the contract means the site's base size.

import { defaultTokens, type TokenSet } from './defaultTokens'
import { fontFamily } from './fontStacks'

export type TokenKind = 'color' | 'space' | 'text'

const scale = (rem: number): string => `calc(var(--qb-base-size) * ${rem})`

function entries(set: Readonly<TokenSet>): [string, string][] {
  return [
    ...set.colors.map((c): [string, string] => [`--qb-color-${c.id}`, c.value]),
    ...set.typography.sizes.map((s): [string, string] => [`--qb-text-${s.id}`, scale(s.rem)]),
    ...set.spacing.map((s): [string, string] => [`--qb-space-${s.id}`, scale(s.rem)]),
    ['--qb-font-heading', fontFamily(set.typography.headingFont)],
    ['--qb-font-body', fontFamily(set.typography.bodyFont)],
    ['--qb-base-size', `${set.typography.baseSizePx}px`],
    ...widths(set),
  ]
}

// Content widths are pixels, not scaled by the base size: they are layout, not type. A set
// saved before they existed has none, so the default set's widths stand (design-tokens.md §2).
function widths(set: Readonly<TokenSet>): [string, string][] {
  const w = set.contentWidths ?? defaultTokens.contentWidths
  if (!w) return []
  return [
    ['--qb-width-narrow', `${w.narrow}px`],
    ['--qb-width-normal', `${w.normal}px`],
    ['--qb-width-wide', `${w.wide}px`],
  ]
}

/**
 * The custom properties for a token set. Every id of the default set is always defined —
 * with the built-in value when the site's set lacks it — so a fallback to a property's
 * default always resolves, even when that default token was deleted too (data-model §2).
 */
export function tokenCss(set: Readonly<TokenSet>): Record<string, string> {
  return Object.fromEntries([...entries(defaultTokens), ...entries(set)])
}

/** The custom property name for a token id. */
export const tokenVar = (kind: TokenKind, id: string): string => `--qb-${kind}-${id}`

/** Whether the set holds a token of this kind and id. */
export function tokenExists(set: Readonly<TokenSet>, kind: TokenKind, id: string): boolean {
  const list = kind === 'color' ? set.colors : kind === 'text' ? set.typography.sizes : set.spacing
  return list.some((t) => t.id === id)
}

/**
 * The CSS value for a token reference: the token's variable, or the property default's when
 * the token no longer exists (clarification Q4). With neither, the given fallback.
 */
export function tokenValue(
  ref: unknown,
  kind: TokenKind,
  set: Readonly<TokenSet>,
  defaultRef: unknown,
  fallback = 'initial',
): string {
  const idOf = (r: unknown): string | null =>
    typeof r === 'object' && r !== null && typeof (r as { token?: unknown }).token === 'string' ? (r as { token: string }).token : null

  const id = idOf(ref)
  if (id && tokenExists(set, kind, id)) return `var(${tokenVar(kind, id)})`

  const fallbackId = idOf(defaultRef)
  return fallbackId ? `var(${tokenVar(kind, fallbackId)})` : fallback
}

/** Whether a reference names a token the set no longer holds: the panel marks these. */
export function isDeletedToken(ref: unknown, kind: TokenKind, set: Readonly<TokenSet>): boolean {
  const id = typeof ref === 'object' && ref !== null ? (ref as { token?: unknown }).token : undefined
  return typeof id === 'string' && !tokenExists(set, kind, id)
}
