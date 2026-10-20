// The token set a site uses until it saves its own (011 research ED-14, FR-032).
//
// Imported from the design token contract's own golden file — the same file the server
// embeds — so the editor, the server and the contract share one copy.

import defaults from '@contracts/design-tokens/examples/valid/default.json'

export interface TokenEntry {
  id: string
  name: string
}

export interface ColorToken extends TokenEntry {
  value: string
}

export interface ScaleToken extends TokenEntry {
  rem: number
}

/** The three named content widths, in CSS pixels (design-tokens.md §2). */
export interface ContentWidths {
  narrow: number
  normal: number
  wide: number
}

/** How menu entries look (024 FR-017b). */
export type MenuLook = 'link' | 'underline' | 'pill' | 'button'

/** The colours a menu style may set, each a colour token id (024 FR-017c). */
export const MENU_COLOR_KEYS = ['background', 'text', 'hoverText', 'hoverBackground', 'currentText', 'currentBackground', 'focus', 'panelBackground'] as const
export type MenuColorKey = (typeof MENU_COLOR_KEYS)[number]

/** The site-wide menu style (024 FR-017a; specs/024-navigation/contracts/menu-style.md). */
export interface MenuStyle {
  look?: MenuLook
  colors?: Partial<Record<MenuColorKey, string>>
}

/** A design token set (docs/contracts/design-tokens.md). */
export interface TokenSet {
  schemaVersion: number
  colors: ColorToken[]
  typography: {
    headingFont: string
    bodyFont: string
    baseSizePx: number
    sizes: ScaleToken[]
  }
  spacing: ScaleToken[]
  /** Optional: a set saved before content widths existed has none, and takes the defaults. */
  contentWidths?: ContentWidths
  /** Optional: absent, menus take the built-in style. */
  menuStyle?: MenuStyle
}

export const defaultTokens: Readonly<TokenSet> = defaults as TokenSet
