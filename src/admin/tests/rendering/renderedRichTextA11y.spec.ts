import { afterEach, describe, expect, it } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import axe from 'axe-core'

// 035 SC-003: the published HTML of every rich text format passes the accessibility checks:
// heading levels, image alternative text, table headers. Each golden page the server's
// RendererGoldenTests checked in is loaded as it is served.

const golden = resolve(process.cwd(), '../../tests/fixtures/rendering/golden')
const pages = readdirSync(golden).filter((f) => f.startsWith('rich-text-') && f.endsWith('.html')).sort()

const RULES = ['heading-order', 'image-alt', 'th-has-data-cells', 'td-has-header', 'scrollable-region-focusable', 'list', 'listitem', 'link-name', 'nested-interactive']

afterEach(() => {
  document.documentElement.innerHTML = '<head></head><body></body>'
})

describe('published rich text', () => {
  it('covers every rich text golden page', () => {
    expect(pages.length).toBeGreaterThanOrEqual(8)
  })

  it.each(pages)('%s passes the accessibility checks', async (page) => {
    const html = readFileSync(join(golden, page), 'utf8')
    document.documentElement.innerHTML = html.replace(/^<!doctype html>/i, '').replace(/<\/?html[^>]*>/gi, '')

    const results = await axe.run(document, { runOnly: { type: 'rule', values: RULES } })

    expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.html).join(' | ')}`)).toEqual([])
    // The limits page holds 4,000 table cells: axe needs longer than the default on a busy machine.
  }, 30_000)
})
