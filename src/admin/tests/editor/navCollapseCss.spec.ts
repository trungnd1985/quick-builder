import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// 024 FR-017: a Navigation block set to collapse shows its menu button only below the chosen
// width. The button is a <summary>, and a broader ".qb-nav summary" rule once outranked the
// rule hiding it, so the button showed at every width. jsdom applies no container queries, so
// the cascade for the button's `display` is resolved here from the stylesheet itself:
// specificity first, then source order, with each @container rule applying only when its
// condition holds for the given page width.

const css = readFileSync(resolve(process.cwd(), 'src/editor/blocks/blocks.css'), 'utf8')

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
        stack.push(width ? Number(width[1]) : prelude.startsWith('@container') ? 'other' : null)
        continue
      }
      const end = text.indexOf('}', i)
      const widths = stack.filter((s): s is number => typeof s === 'number')
      if (!stack.includes('other')) {
        rules.push({
          selectors: prelude.split(',').map((s) => s.trim()),
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

/** Specificity as [ids, classes/attributes/pseudo-classes, elements]. */
function specificity(selector: string): number[] {
  const bare = selector.replace(/::[\w-]+/g, ' x')
  const ids = (bare.match(/#[\w-]+/g) ?? []).length
  const classes = (bare.match(/\.[\w-]+|\[[^\]]+\]|:(?!:)[\w-]+/g) ?? []).length
  const elements = (bare.replace(/[.#][\w-]+|\[[^\]]+\]|:[\w-]+(\([^)]*\))?/g, ' ').match(/(^|[\s>+~])[a-z][\w-]*/gi) ?? []).length
  return [ids, classes, elements]
}

/** Whether the element matches; selectors jsdom cannot parse match nothing. */
function matchesSafely(element: Element, selector: string): boolean {
  try {
    return element.matches(selector)
  } catch {
    return false
  }
}

const compare = (a: number[], b: number[]) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2]

/** The `display` the menu button gets on a page of the given width. */
function toggleDisplay(collapse: 'mobile' | 'tablet', pageWidth: number): string {
  document.body.innerHTML = `
    <div class="qb-page"><nav class="qb-nav qb-nav--horizontal qb-nav--align-start qb-nav--collapse-${collapse}">
      <details class="qb-nav__panel" open><summary class="qb-nav__toggle">Menu</summary></details>
    </nav></div>`
  const toggle = document.querySelector('.qb-nav__toggle')!

  let winner: { display: string; specificity: number[]; order: number } | null = null
  for (const rule of parse(css)) {
    if (rule.maxWidth !== null && pageWidth > rule.maxWidth) continue
    const display = /(?:^|;)\s*display:\s*([^;]+)/.exec(rule.declarations)?.[1].trim()
    if (!display) continue
    for (const selector of rule.selectors) {
      if (!matchesSafely(toggle, selector)) continue
      const s = specificity(selector)
      if (!winner || compare(s, winner.specificity) > 0 || (compare(s, winner.specificity) === 0 && rule.order > winner.order)) {
        winner = { display, specificity: s, order: rule.order }
      }
    }
  }
  return winner?.display ?? 'unset'
}

describe('the collapsing menu button', () => {
  it('stays hidden on wide pages', () => {
    expect(toggleDisplay('mobile', 2560)).toBe('none')
    expect(toggleDisplay('tablet', 2560)).toBe('none')
    expect(toggleDisplay('mobile', 768)).toBe('none')
    expect(toggleDisplay('tablet', 768)).toBe('none')
  })

  it('shows below the width the block chose, and only there', () => {
    expect(toggleDisplay('tablet', 767)).toBe('inline-block')
    expect(toggleDisplay('mobile', 767)).toBe('none')
    expect(toggleDisplay('mobile', 480)).toBe('inline-block')
    expect(toggleDisplay('tablet', 480)).toBe('inline-block')
  })
})
