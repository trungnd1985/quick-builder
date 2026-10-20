// Contrast warnings for menu colours (024 FR-017d; contracts/menu-style.md §4).
//
// The editor warns, and never refuses, when a menu's colour pairs fall below the WCAG 2.x
// minimums. Colours are resolved as the stylesheet resolves them — the block's override, else
// the site's menu style, else the built-in default for the look — and translucent colours are
// composited over what lies beneath: the bar over the page background, hover and current
// backgrounds over the bar.

import { defaultTokens, type MenuColorKey, type MenuLook, type TokenSet } from './defaultTokens'

export interface Rgba {
  r: number
  g: number
  b: number
  /** 0 to 1. */
  a: number
}

/** A token value as a colour: `#rrggbb`, `#rrggbbaa` or `transparent`; null for anything else. */
export function parseColor(value: string): Rgba | null {
  if (value === 'transparent') return { r: 0, g: 0, b: 0, a: 0 }
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(value)
  if (!match) return null
  const hex = match[1]
  return {
    r: parseInt(hex.slice(0, 2), 16),
    g: parseInt(hex.slice(2, 4), 16),
    b: parseInt(hex.slice(4, 6), 16),
    a: match[2] ? parseInt(match[2], 16) / 255 : 1,
  }
}

/** A colour seen over an opaque background. */
export function composite(top: Rgba, beneath: Rgba): Rgba {
  const mix = (a: number, b: number) => Math.round(a * top.a + b * (1 - top.a))
  return { r: mix(top.r, beneath.r), g: mix(top.g, beneath.g), b: mix(top.b, beneath.b), a: 1 }
}

function luminance({ r, g, b }: Rgba): number {
  const channel = (v: number) => {
    const c = v / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

/** The WCAG 2.x contrast ratio of two opaque colours, from 1 to 21. */
export function contrastRatio(a: Rgba, b: Rgba): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (light + 0.05) / (dark + 0.05)
}

const toHex = ({ r, g, b }: Rgba) => `#${[r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('')}`

const LOOKS: readonly MenuLook[] = ['link', 'underline', 'pill', 'button']

/** The Navigation block props that override each site colour (contracts/menu-style.md §3). */
const BLOCK_PROPS: Record<MenuColorKey, string> = {
  text: 'color',
  currentText: 'activeColor',
  background: 'background',
  hoverText: 'hoverColor',
  hoverBackground: 'hoverBackground',
  currentBackground: 'activeBackground',
  focus: 'focusColor',
  panelBackground: 'panelBackground',
}

/** A menu's colours as visitors see them: opaque, as `#rrggbb`. */
export interface EffectiveMenuColors {
  look: MenuLook
  background: string
  text: string
  hoverText: string
  hoverBackground: string
  currentText: string
  currentBackground: string
  focus: string
  panelBackground: string
}

/**
 * The colours a Navigation block shows, given the draft tokens and the block's props (an
 * empty object for the site-wide style alone).
 */
export function effectiveMenuColors(tokens: Readonly<TokenSet>, props: Record<string, unknown>): EffectiveMenuColors {
  const site = tokens.menuStyle
  const ownLook = typeof props.look === 'string' && (LOOKS as readonly string[]).includes(props.look) ? (props.look as MenuLook) : null
  const look: MenuLook = ownLook ?? (site?.look && LOOKS.includes(site.look) ? site.look : 'link')
  const filled = look === 'pill' || look === 'button'

  // A token of the site's set; a built-in id falls back to the default set, as the token CSS does.
  const valueOf = (id: string, builtIn = false): string | null =>
    tokens.colors.find((c) => c.id === id)?.value ?? (builtIn ? defaultTokens.colors.find((c) => c.id === id)?.value ?? null : null)
  const blockValue = (key: MenuColorKey): string | null => {
    const ref = props[BLOCK_PROPS[key]]
    const id = typeof ref === 'object' && ref !== null ? (ref as { token?: unknown }).token : null
    return typeof id === 'string' ? valueOf(id) : null
  }
  const siteValue = (key: MenuColorKey): string | null => {
    const id = site?.colors?.[key]
    return typeof id === 'string' ? valueOf(id) : null
  }
  const chosen = (key: MenuColorKey, fallback: string): string => blockValue(key) ?? siteValue(key) ?? fallback

  const builtIn = (id: string) => valueOf(id, true) ?? '#000000'
  const lookText = filled ? builtIn('color-text') : builtIn('color-brand')
  const lookBackground = filled ? builtIn('color-brand-light') : 'transparent'

  const color = (value: string) => parseColor(value) ?? parseColor('#000000')!
  const page = color(builtIn('color-background'))
  const bar = composite(color(chosen('background', 'transparent')), page)
  const over = (value: string, beneath: Rgba) => toHex(composite(color(value), beneath))

  const currentText = chosen('currentText', lookText)
  // Focus falls back to the block's current text, then the site's focus and current text.
  const focus = blockValue('focus') ?? blockValue('currentText') ?? siteValue('focus') ?? siteValue('currentText') ?? builtIn('color-brand')

  return {
    look,
    background: toHex(bar),
    text: over(chosen('text', builtIn('color-text')), bar),
    hoverText: over(chosen('hoverText', lookText), bar),
    hoverBackground: over(chosen('hoverBackground', lookBackground), bar),
    currentText: over(currentText, bar),
    currentBackground: over(chosen('currentBackground', lookBackground), bar),
    focus: over(focus, bar),
    panelBackground: over(chosen('panelBackground', builtIn('color-background')), page),
  }
}

export type ContrastPair = 'text' | 'hover' | 'current' | 'panel' | 'focus'

export interface ContrastProblem {
  pair: ContrastPair
  foreground: string
  background: string
  ratio: number
  minimum: number
}

/** The pairs below their minimum, in the order of contracts/menu-style.md §4. */
export function menuContrastProblems(colors: EffectiveMenuColors): ContrastProblem[] {
  const pairs: [ContrastPair, string, string, number][] = [
    ['text', colors.text, colors.background, 4.5],
    ['hover', colors.hoverText, colors.hoverBackground, 4.5],
    ['current', colors.currentText, colors.currentBackground, 4.5],
    ['panel', colors.text, colors.panelBackground, 4.5],
    ['focus', colors.focus, colors.background, 3],
  ]
  return pairs.flatMap(([pair, foreground, background, minimum]) => {
    const ratio = contrastRatio(parseColor(foreground)!, parseColor(background)!)
    return ratio < minimum ? [{ pair, foreground, background, ratio, minimum }] : []
  })
}
