import { afterEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// 011 research ED-01: the admin's element rules style the admin, never the canvas, where a
// site's own blocks are drawn. jsdom has no layout, but it does compute cascaded styles.

const adminCss = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')

describe('canvas style isolation', () => {
  afterEach(() => {
    document.head.innerHTML = ''
    document.body.innerHTML = ''
  })

  it('applies the admin heading and button rules outside the canvas only', () => {
    const style = document.createElement('style')
    style.textContent = adminCss
    document.head.appendChild(style)
    document.body.innerHTML = `
      <h1 id="admin-h1">Admin</h1><button id="admin-button">Admin</button>
      <div class="qb-canvas"><h1 id="canvas-h1">Page</h1><button id="canvas-button">Page</button></div>`

    const size = (id: string) => getComputedStyle(document.getElementById(id)!).fontSize
    const cursor = (id: string) => getComputedStyle(document.getElementById(id)!).cursor

    expect(size('admin-h1')).toBe('24px')
    expect(size('canvas-h1')).not.toBe('24px')
    expect(cursor('admin-button')).toBe('pointer')
    expect(cursor('canvas-button')).not.toBe('pointer')
  })

  it('leaves no element rule in the admin stylesheet unguarded', () => {
    // Every rule whose selector starts with an element name ends in the canvas guard.
    const selectors = adminCss
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .split('}')
      .map((block) => block.split('{')[0].trim())
      .flatMap((selector) => selector.split(',').map((s) => s.trim()))
      .filter((s) => /^(h[1-6]|label|input|select|button|textarea|p|a|ul|ol|img|table)\b/.test(s))

    expect(selectors.length).toBeGreaterThan(0)
    for (const selector of selectors) {
      expect(selector.endsWith(':where(:not(.qb-canvas *))') || selector.startsWith('a.button-link'), selector).toBe(true)
    }
  })
})
