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
}

export const defaultTokens: Readonly<TokenSet> = defaults as TokenSet
