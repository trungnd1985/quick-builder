import { describe, expect, it } from 'vitest'
import { composite, contrastRatio, effectiveMenuColors, menuContrastProblems, parseColor } from '../../src/editor/tokens/contrast'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'

// 024 FR-017d (tasks T091; contracts/menu-style.md §4): the editor warns about menu colour
// pairs below the WCAG 2.x minimums, resolving token ids and the look's own defaults, and
// compositing translucent colours over what lies beneath.

const tokens = (extra: Partial<TokenSet> = {}, colors: Record<string, string> = {}): TokenSet => ({
  ...defaultTokens,
  colors: [
    ...defaultTokens.colors.filter((c) => !(c.id in colors)),
    ...Object.entries(colors).map(([id, value]) => ({ id, name: id, value })),
  ],
  ...extra,
})

describe('contrast ratios', () => {
  it('match WCAG 2.x', () => {
    expect(contrastRatio(parseColor('#000000')!, parseColor('#ffffff')!)).toBeCloseTo(21, 5)
    expect(contrastRatio(parseColor('#ffffff')!, parseColor('#ffffff')!)).toBeCloseTo(1, 5)
    expect(contrastRatio(parseColor('#767676')!, parseColor('#ffffff')!)).toBeCloseTo(4.54, 2)
    // Order does not matter.
    expect(contrastRatio(parseColor('#ffffff')!, parseColor('#767676')!)).toBeCloseTo(4.54, 2)
  })

  it('composites translucent colours over what lies beneath', () => {
    const half = parseColor('#00000080')!
    expect(composite(half, parseColor('#ffffff')!)).toEqual({ r: 127, g: 127, b: 127, a: 1 })
    expect(composite(parseColor('transparent')!, parseColor('#123456')!)).toEqual({ r: 0x12, g: 0x34, b: 0x56, a: 1 })
    expect(parseColor('not a colour')).toBeNull()
  })
})

describe('effective menu colours', () => {
  it('take the block override, else the site value, else the built-in default for the look', () => {
    const set = tokens({ menuStyle: { look: 'pill', colors: { text: 'color-muted', background: 'color-gone' } } })

    const site = effectiveMenuColors(set, {})
    expect(site.look).toBe('pill')
    expect(site.text).toBe('#6b7280')
    // A missing token falls back to the default: a transparent bar over the page background.
    expect(site.background).toBe('#ffffff')
    // Pill: hover and current backgrounds default to the light brand colour.
    expect(site.hoverBackground).toBe('#dbeafe')

    const block = effectiveMenuColors(set, { look: 'link', color: { token: 'color-brand' } })
    expect(block.look).toBe('link')
    expect(block.text).toBe('#2563eb')
    // Link: no background of its own, so the bar's shows through.
    expect(block.hoverBackground).toBe('#ffffff')
  })

  it('give focus the current text colour, the block\'s first, when none is chosen', () => {
    const set = tokens({ menuStyle: { colors: { currentText: 'color-muted' } } })
    expect(effectiveMenuColors(set, {}).focus).toBe('#6b7280')
    expect(effectiveMenuColors(set, { activeColor: { token: 'color-text' } }).focus).toBe('#111827')
    expect(effectiveMenuColors(set, { activeColor: { token: 'color-text' }, focusColor: { token: 'color-brand' } }).focus).toBe('#2563eb')
  })
})

describe('menu contrast problems', () => {
  it('finds none in the built-in style of any look', () => {
    for (const look of ['link', 'underline', 'pill', 'button'] as const) {
      expect(menuContrastProblems(effectiveMenuColors(defaultTokens, { look }))).toEqual([])
    }
  })

  it('names each failing pair with its minimum', () => {
    const set = tokens({ menuStyle: { look: 'pill', colors: { text: 'color-pale', hoverText: 'color-pale', focus: 'color-pale' } } }, { 'color-pale': '#eeeeee' })

    const problems = menuContrastProblems(effectiveMenuColors(set, {}))
    expect(problems.map((p) => p.pair)).toEqual(['text', 'hover', 'panel', 'focus'])
    expect(problems.find((p) => p.pair === 'focus')!.minimum).toBe(3)
    expect(problems.find((p) => p.pair === 'text')!.minimum).toBe(4.5)
    expect(problems.every((p) => p.ratio < p.minimum)).toBe(true)
  })

  it('checks hover text against the look\'s own default background', () => {
    // White text on the light brand colour fails for pill, but on a dark bar passes for link.
    const set = (look: 'pill' | 'link') => tokens({ menuStyle: { look, colors: { background: 'color-text', text: 'color-background', hoverText: 'color-background', currentText: 'color-background', panelBackground: 'color-text' } } })

    expect(menuContrastProblems(effectiveMenuColors(set('pill'), {})).map((p) => p.pair)).toEqual(['hover', 'current'])
    expect(menuContrastProblems(effectiveMenuColors(set('link'), {}))).toEqual([])
  })
})
