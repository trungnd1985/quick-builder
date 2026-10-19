import { afterEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// 024 US5 (tasks T068, FR-019 to FR-020a): the published navigation script, as checked in by
// the server's NavScriptTests, run against markup shaped as NavigationRenderer writes it.
// Escape closes the innermost submenu, opening one closes its siblings, an outside click closes
// horizontal dropdowns, the panel follows the container's width, and opening a collapsed
// horizontal panel opens the current path.

const script = readFileSync(resolve(process.cwd(), '../../tests/fixtures/rendering/navigation/nav-script.js'), 'utf8')

// Products (a link with a submenu holding Chairs, the current page) and Company (a group).
const menu = (layout: 'horizontal' | 'vertical', collapse = '') => `
  <div class="qb-page">
    <nav class="qb-nav qb-nav--${layout} qb-nav--align-start${collapse ? ` qb-nav--collapse-${collapse}` : ''}" aria-label="Main" data-qb-nav="n1"${collapse ? ` data-qb-collapse="${collapse}"` : ''}>
      ${collapse ? '<details class="qb-nav__panel" open><summary class="qb-nav__toggle">Menu</summary>' : ''}
      <ul class="qb-nav__list qb-nav__list--l1">
        <li class="qb-nav__item qb-nav__item--containing" data-qb-current-path="">
          <a class="qb-nav__link" href="/products">Products</a>
          <details class="qb-nav__sub" name="qb-n1-l1"${layout === 'vertical' ? ' open' : ''}>
            <summary class="qb-nav__expander" id="products-toggle"><span class="qb-nav__hidden">Show submenu: Products</span></summary>
            <ul class="qb-nav__list qb-nav__list--l2">
              <li class="qb-nav__item qb-nav__item--current"><a class="qb-nav__link" id="chairs" href="/products/chairs" aria-current="page">Chairs</a></li>
            </ul>
          </details>
        </li>
        <li class="qb-nav__item">
          <details class="qb-nav__sub" name="qb-n1-l1">
            <summary class="qb-nav__expander qb-nav__expander--label" id="company-toggle">Company</summary>
            <ul class="qb-nav__list qb-nav__list--l2">
              <li class="qb-nav__item"><a class="qb-nav__link" id="team" href="/team">Team</a></li>
            </ul>
          </details>
        </li>
      </ul>
      ${collapse ? '</details>' : ''}
    </nav>
    <p id="outside">Elsewhere</p>
  </div>`

/** Puts the markup in the page, gives its page container a width, and runs the script once. */
function load(html: string, width = 1024): HTMLElement {
  document.body.innerHTML = html
  const page = document.querySelector<HTMLElement>('.qb-page')!
  Object.defineProperty(page, 'clientWidth', { configurable: true, value: width })
  new Function(script)()
  return document.querySelector<HTMLElement>('nav.qb-nav')!
}

const details = (nav: HTMLElement, summaryId: string) => nav.querySelector<HTMLDetailsElement>(`#${summaryId}`)!.parentElement as HTMLDetailsElement

/** Opens a disclosure as a click on its summary would, toggle event included. */
function open(element: HTMLDetailsElement): void {
  element.open = true
  element.dispatchEvent(new Event('toggle'))
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('the published navigation script', () => {
  it('is the checked-in script, small enough to inline', () => {
    expect(script.length).toBeGreaterThan(0)
    expect(new TextEncoder().encode(script.trim()).length).toBeLessThanOrEqual(2048)
  })

  it('closes the innermost open submenu on Escape and returns the focus to its button', () => {
    const nav = load(menu('horizontal'))
    const company = details(nav, 'company-toggle')
    open(company)

    const team = nav.querySelector<HTMLElement>('#team')!
    team.focus()
    team.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

    expect(company.open).toBe(false)
    expect(document.activeElement?.id).toBe('company-toggle')
  })

  it('closes the sibling submenus of the same level when one opens', () => {
    const nav = load(menu('horizontal'))
    const products = details(nav, 'products-toggle')
    const company = details(nav, 'company-toggle')

    open(products)
    open(company)

    expect(company.open).toBe(true)
    expect(products.open).toBe(false)
  })

  it('closes horizontal dropdowns on a click outside them, but not on a click inside', () => {
    const nav = load(menu('horizontal'))
    const company = details(nav, 'company-toggle')
    open(company)

    nav.querySelector<HTMLElement>('#team')!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(company.open).toBe(true)

    document.getElementById('outside')!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(company.open).toBe(false)
  })

  it('leaves vertical submenus open on an outside click: they are part of the page', () => {
    const nav = load(menu('vertical'))
    const products = details(nav, 'products-toggle')
    expect(products.open).toBe(true)

    document.getElementById('outside')!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(products.open).toBe(true)
  })

  it('closes the panel in a container at or below the collapse width, and keeps it open above', () => {
    const narrow = load(menu('horizontal', 'mobile'), 480)
    expect(narrow.querySelector<HTMLDetailsElement>('.qb-nav__panel')!.open).toBe(false)

    const wide = load(menu('horizontal', 'mobile'), 481)
    expect(wide.querySelector<HTMLDetailsElement>('.qb-nav__panel')!.open).toBe(true)

    const tablet = load(menu('horizontal', 'tablet'), 767)
    expect(tablet.querySelector<HTMLDetailsElement>('.qb-nav__panel')!.open).toBe(false)
  })

  it('opens the current path when a collapsed horizontal panel is opened (FR-019a)', () => {
    const nav = load(menu('horizontal', 'mobile'), 320)
    const panel = nav.querySelector<HTMLDetailsElement>('.qb-nav__panel')!
    const products = details(nav, 'products-toggle')
    const company = details(nav, 'company-toggle')
    expect(panel.open).toBe(false)
    expect(products.open).toBe(false)

    open(panel)

    expect(products.open).toBe(true)
    expect(company.open).toBe(false)
  })
})
