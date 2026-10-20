import { afterEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// 024 FR-017 to FR-017d: what the Navigation block's stylesheet gives each part of a menu, in
// each look and state. jsdom applies no container queries, no :hover and no :visited, so the
// cascade is resolved here from blocks.css itself: a rule applies when its selector matches
// the element in the asked state (:hover, :focus-visible and :visited are mapped to
// attributes set on the element) and its @container condition holds for the page width; the
// winner is the highest specificity, then the last in source order.
//
// The collapsing menu button is here too: a ".qb-nav summary" rule once outranked the rule
// hiding it, so it showed at every width.

const css = readFileSync(resolve(process.cwd(), 'src/editor/blocks/blocks.css'), 'utf8')

type State = 'hover' | 'focus-visible' | 'visited'
const STATES: State[] = ['hover', 'focus-visible', 'visited']

interface Rule {
  selectors: string[]
  declarations: string
  /** The page widths this rule applies at; null for unconditional rules. */
  maxWidth: number | null
  order: number
}

/** Every style rule, with the max-width of the @container query around it, if any. */
function parse(source: string): Rule[] {
  const text = source.replace(/\/\*[\s\S]*?\*\//g, '')
  const rules: Rule[] = []
  const stack: (number | null | 'other')[] = []
  let start = 0
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (c === '{') {
      const prelude = text.slice(start, i).trim()
      start = i + 1
      if (prelude.startsWith('@')) {
        const width = /^@container\s+page\s*\(max-width:\s*(\d+)px\)/.exec(prelude)
        stack.push(width ? Number(width[1]) : prelude.startsWith('@container') || prelude.startsWith('@media') ? 'other' : null)
        continue
      }
      const end = text.indexOf('}', i)
      const widths = stack.filter((s): s is number => typeof s === 'number')
      if (!stack.includes('other')) {
        rules.push({
          selectors: splitTopLevel(prelude),
          declarations: text.slice(i + 1, end),
          maxWidth: widths.length > 0 ? Math.min(...widths) : null,
          order: rules.length,
        })
      }
      i = end
      start = end + 1
    } else if (c === '}') {
      stack.pop()
      start = i + 1
    }
  }
  return rules
}

/** Splits on commas outside parentheses. */
function splitTopLevel(list: string): string[] {
  const parts: string[] = []
  let depth = 0
  let from = 0
  for (let i = 0; i < list.length; i++) {
    if (list[i] === '(') depth++
    else if (list[i] === ')') depth--
    else if (list[i] === ',' && depth === 0) {
      parts.push(list.slice(from, i).trim())
      from = i + 1
    }
  }
  parts.push(list.slice(from).trim())
  return parts.filter(Boolean)
}

/** The selector with the state pseudo-classes as attributes jsdom can match. */
const forState = (selector: string): string =>
  STATES.reduce((s, state) => s.replaceAll(`:${state}`, `[data-state-${state}]`), selector)

/** Specificity as [ids, classes/attributes/pseudo-classes, elements]; :is and :not take their most specific argument, :where none. */
function specificity(selector: string): number[] {
  let rest = selector.replace(/::[\w-]+/g, ' x')
  const extra = [0, 0, 0]
  // Innermost functional pseudo-classes first.
  for (;;) {
    const match = /:(is|not|where)\(([^()]*)\)/.exec(rest)
    if (!match) break
    if (match[1] !== 'where') {
      const best = splitTopLevel(match[2]).map(specificity).sort((a, b) => compare(b, a))[0] ?? [0, 0, 0]
      best.forEach((v, i) => (extra[i] += v))
    }
    rest = rest.slice(0, match.index) + rest.slice(match.index + match[0].length)
  }
  const ids = (rest.match(/#[\w-]+/g) ?? []).length
  const classes = (rest.match(/\.[\w-]+|\[[^\]]+\]|:(?!:)[\w-]+/g) ?? []).length
  const elements = (rest.replace(/[.#][\w-]+|\[[^\]]+\]|:[\w-]+/g, ' ').match(/(^|[\s>+~])[a-z][\w-]*/gi) ?? []).length
  return [ids + extra[0], classes + extra[1], elements + extra[2]]
}

const compare = (a: number[], b: number[]) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2]

function matchesSafely(element: Element, selector: string): boolean {
  try {
    return element.matches(selector)
  } catch {
    return false
  }
}

const rules = parse(css)

/** The declared value of `property` the element gets in the given states, or 'unset'. */
function declared(element: Element, property: string, options: { width?: number; states?: State[] } = {}): string {
  const width = options.width ?? 1280
  for (const state of STATES) element.toggleAttribute(`data-state-${state}`, options.states?.includes(state) === true)

  let winner: { value: string; specificity: number[]; order: number } | null = null
  const pattern = new RegExp(`(?:^|;)\\s*${property}:\\s*([^;]+)`)
  for (const rule of rules) {
    if (rule.maxWidth !== null && width > rule.maxWidth) continue
    const value = pattern.exec(rule.declarations)?.[1].trim()
    if (!value) continue
    for (const selector of rule.selectors) {
      // A state pseudo-class the element is not in never matches.
      if (!matchesSafely(element, forState(selector))) continue
      const s = specificity(selector)
      if (!winner || compare(s, winner.specificity) > 0 || (compare(s, winner.specificity) === 0 && rule.order > winner.order)) {
        winner = { value, specificity: s, order: rule.order }
      }
    }
  }
  return winner?.value.replace(/\s+/g, ' ') ?? 'unset'
}

type Look = 'link' | 'underline' | 'pill' | 'button'

/** A horizontal menu: Home (current), Shop with a submenu holding Chairs. */
function mount(look: Look, collapse = ''): Record<'nav' | 'home' | 'shop' | 'chairs' | 'toggle' | 'panelList' | 'subList', Element> {
  document.body.innerHTML = `
    <div class="qb-page"><nav class="qb-nav qb-nav--horizontal qb-nav--align-start${collapse ? ` qb-nav--collapse-${collapse}` : ''} qb-nav--look-${look}">
      <details class="qb-nav__panel" open><summary class="qb-nav__toggle">Menu</summary>
      <ul class="qb-nav__list qb-nav__list--l1">
        <li class="qb-nav__item qb-nav__item--current"><a class="qb-nav__link" id="home" href="/" aria-current="page">Home</a></li>
        <li class="qb-nav__item"><a class="qb-nav__link" id="shop" href="/shop">Shop</a>
          <details class="qb-nav__sub" name="qb-n1-l1"><summary class="qb-nav__expander"><span class="qb-nav__hidden">Show submenu: Shop</span></summary>
            <ul class="qb-nav__list qb-nav__list--l2" id="sub"><li class="qb-nav__item"><a class="qb-nav__link" id="chairs" href="/shop/chairs">Chairs</a></li></ul>
          </details></li>
      </ul></details>
    </nav></div>`
  const q = (s: string) => document.querySelector(s)!
  return { nav: q('nav'), home: q('#home'), shop: q('#shop'), chairs: q('#chairs'), toggle: q('.qb-nav__toggle'), panelList: q('.qb-nav__list--l1'), subList: q('#sub') }
}

afterEach(() => {
  document.body.innerHTML = ''
})

const TEXT = 'var(--qb-nav-text, var(--qb-menu-text, var(--qb-color-color-text)))'
const HOVER_TEXT = 'var(--qb-nav-hover-text, var(--qb-menu-hover-text, var(--qb-nav-look-text, var(--qb-color-color-brand))))'
const CURRENT_TEXT = 'var(--qb-nav-current-text, var(--qb-menu-current-text, var(--qb-nav-look-text, var(--qb-color-color-brand))))'
const HOVER_BG = 'var(--qb-nav-hover-background, var(--qb-menu-hover-background, var(--qb-nav-look-background, transparent)))'
const CURRENT_BG = 'var(--qb-nav-current-background, var(--qb-menu-current-background, var(--qb-nav-look-background, transparent)))'
const FOCUS = 'var(--qb-nav-focus, var(--qb-nav-current-text, var(--qb-menu-focus, var(--qb-menu-current-text, var(--qb-color-color-brand)))))'
const PANEL = 'var(--qb-nav-panel-background, var(--qb-menu-panel-background, var(--qb-color-color-background)))'
const LOOKS: Look[] = ['link', 'underline', 'pill', 'button']

describe('the collapsing menu button', () => {
  it('stays hidden on wide pages', () => {
    for (const collapse of ['mobile', 'tablet']) {
      for (const width of [2560, 768]) {
        expect(declared(mount('link', collapse).toggle, 'display', { width })).toBe('none')
      }
    }
  })

  it('shows below the width the block chose, and only there', () => {
    expect(declared(mount('link', 'tablet').toggle, 'display', { width: 767 })).toBe('inline-block')
    expect(declared(mount('link', 'mobile').toggle, 'display', { width: 767 })).toBe('none')
    expect(declared(mount('link', 'mobile').toggle, 'display', { width: 480 })).toBe('inline-block')
    expect(declared(mount('link', 'tablet').toggle, 'display', { width: 480 })).toBe('inline-block')
  })
})

describe('menu colours, in every look (FR-017a, FR-017c)', () => {
  it('takes the bar and dropdown backgrounds from the block, else the site, else the default', () => {
    for (const look of LOOKS) {
      const m = mount(look)
      expect(declared(m.nav, 'background-color')).toBe('var(--qb-nav-background, var(--qb-menu-background, transparent))')
      expect(declared(m.subList, 'background-color')).toBe(PANEL)
    }
  })

  it('gives the small-screen panel the dropdown background when collapsed', () => {
    expect(declared(mount('link', 'mobile').panelList, 'background-color', { width: 375 })).toBe(PANEL)
  })

  it('colours entries at rest, on hover and as the current entry', () => {
    for (const look of LOOKS) {
      const m = mount(look)
      expect(declared(m.shop, 'color')).toBe(TEXT)
      expect(declared(m.shop, 'color', { states: ['hover'] })).toBe(HOVER_TEXT)
      expect(declared(m.home, 'color')).toBe(CURRENT_TEXT)
      expect(declared(m.chairs, 'color', { states: ['hover'] })).toBe(HOVER_TEXT)
    }
  })

  it('never shows a visited colour (FR-017c)', () => {
    for (const look of LOOKS) {
      const m = mount(look)
      expect(declared(m.shop, 'color', { states: ['visited'] })).toBe(TEXT)
      expect(declared(m.home, 'color', { states: ['visited'] })).toBe(CURRENT_TEXT)
      expect(declared(m.shop, 'color', { states: ['visited', 'hover'] })).toBe(HOVER_TEXT)
    }
  })

  it('always draws the focus ring, falling back to the current-entry colour (FR-017d)', () => {
    for (const look of LOOKS) {
      const m = mount(look)
      for (const element of [m.shop, m.home, m.chairs]) {
        expect(declared(element, 'outline', { states: ['focus-visible'] })).toBe(`2px solid ${FOCUS}`)
      }
    }
  })
})

describe('the four looks on top-level entries (FR-017b)', () => {
  it('gives pill and button a background on hover, focus and the current entry; link and underline none of their own', () => {
    for (const look of LOOKS) {
      const m = mount(look)
      expect(declared(m.shop, 'background-color', { states: ['hover'] })).toBe(HOVER_BG)
      expect(declared(m.shop, 'background-color', { states: ['focus-visible'] })).toBe(HOVER_BG)
      expect(declared(m.home, 'background-color')).toBe(CURRENT_BG)
      const filled = look === 'pill' || look === 'button'
      expect(declared(m.nav, '--qb-nav-look-background')).toBe(filled ? 'var(--qb-color-color-brand-light)' : 'unset')
      // Dark text on the light background: brand on light brand is below 4.5:1.
      expect(declared(m.nav, '--qb-nav-look-text')).toBe(filled ? 'var(--qb-color-color-text)' : 'unset')
    }
  })

  it('rounds pills, borders buttons, underlines underline', () => {
    expect(declared(mount('pill').shop, 'border-radius')).toBe('999px')
    expect(declared(mount('button').shop, 'border')).toBe('1px solid currentColor')
    expect(declared(mount('button').shop, 'border-radius')).toBe('4px')
    expect(declared(mount('underline').shop, 'text-decoration', { states: ['hover'] })).toBe('underline')
    expect(declared(mount('underline').home, 'text-decoration')).toBe('underline')
    expect(declared(mount('link').shop, 'border')).toBe('unset')
  })

  it('leaves submenu entries with the list look', () => {
    for (const look of LOOKS) {
      const m = mount(look)
      expect(declared(m.chairs, 'background-color', { states: ['hover'] })).toBe('unset')
      expect(declared(m.chairs, 'border-radius')).toBe('unset')
      expect(declared(m.chairs, 'border')).toBe('unset')
    }
  })
})
