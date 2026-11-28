import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, provide } from 'vue'
import { readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import CanvasChildren from '../../src/editor/canvas/CanvasChildren.vue'
import { STATIC_CANVAS } from '../../src/editor/canvas/staticCanvas'
import type { ContentNode } from '../../src/editor/schema/schema'
import { i18n } from './harness'
import { jsonFiles, pageExamples, readJson } from './contractFiles'

// 012 research PR-10, SC-008, page-schema.md §9: the editor canvas and a published page draw
// every block with the same markup. The published side is RendererGoldenTests' checked-in HTML;
// this draws the same documents on the canvas and compares the two trees after normalising what
// is allowed to differ:
//   - editor-only elements and classes (qb-editor-*, selection, hints) are dropped;
//   - attributes are not compared, except that a node's custom properties are — from its style
//     attribute on the canvas, from its [data-qb] rule in the published style element;
//   - a Button is a link when published and a span on the canvas, and a linked Image is
//     wrapped in its link only when published;
//   - an Embed's body (a placeholder on the canvas, the player when published) is not compared;
//   - component references are dropped on both sides: the canvas draws a shared box around a
//     component's draft, the published page draws its live version in place, and the component
//     document itself (component-page.json) is compared on its own;
//   - a Navigation block is compared as its nav element only (classes and colour properties): the
//     canvas draws the menu's draft from the preview endpoint, the published page its live
//     version, and the published page's one script element (024 NV-14) has no counterpart.

const golden = resolve(process.cwd(), '../../tests/fixtures/rendering/golden')

interface Normalised {
  tag: string
  classes: string[]
  props: Record<string, string>
  text?: string
  children: Normalised[]
}

const EDITOR_ONLY = /^(qb-editor-|qb-node|qb-component-ref)/

function normalise(element: Element, propsOf: (e: Element) => Record<string, string>): Normalised | null {
  const classes = [...element.classList]
  const kept = classes.filter((c) => c.startsWith('qb-') && !EDITOR_ONLY.test(c))

  // An element that is only an editing aid goes; a block that also carries an editing class
  // (the Embed's placeholder look) stays.
  if ((kept.length === 0 && classes.some((c) => EDITOR_ONLY.test(c))) || element.getAttribute('data-qb')?.startsWith('c.')) return null

  // A missing image, or an Embed with no video: a placeholder on the canvas, left out when
  // published (§9).
  if (kept.includes('qb-image') && !element.querySelector('img')) return null
  if (kept.includes('qb-rt-image') && !element.querySelector('img')) return null
  if (kept.includes('qb-embed') && !element.querySelector('iframe, .qb-embed__url')) return null
  if (element.tagName === 'SCRIPT') return null

  const embed = kept.includes('qb-embed')
  const opaque = embed || kept.includes('qb-nav')
  const children = opaque ? [] : childrenOf(element).map((c) => normalise(c, propsOf)).filter((c): c is Normalised => c !== null)
  const tag = kept.includes('qb-button') ? 'button-element' : element.tagName.toLowerCase()

  return {
    tag,
    classes: kept.sort(),
    props: propsOf(element),
    ...(element.children.length === 0 && !opaque ? { text: element.textContent ?? '' } : {}),
    children,
  }
}

/** An image's link wraps it only when published; its image is what is compared. */
function childrenOf(element: Element): Element[] {
  return [...element.children].flatMap((child) =>
    element.classList.contains('qb-image') && child.tagName === 'A' ? [...child.children] : [child])
}

function declarations(text: string): Record<string, string> {
  const result: Record<string, string> = {}
  for (const part of text.split(';')) {
    const colon = part.indexOf(':')
    if (colon < 0) continue
    const name = part.slice(0, colon).trim()
    if (name.startsWith('--qb-') || name === 'aspect-ratio') result[name] = part.slice(colon + 1).trim()
  }
  return result
}

function publishedTree(html: string): Normalised[] {
  const document = new DOMParser().parseFromString(html, 'text/html')
  const rules = new Map<string, Record<string, string>>()
  for (const match of (document.querySelector('style')?.textContent ?? '').matchAll(/\[data-qb="([^"]+)"\]\{([^}]*)\}/g)) {
    rules.set(match[1]!, declarations(match[2]!))
  }

  const page = document.querySelector('.qb-page')!
  return [...page.children]
    .map((e) => normalise(e, (el) => rules.get(el.getAttribute('data-qb') ?? '') ?? {}))
    .filter((n): n is Normalised => n !== null)
}

function canvasTree(root: ContentNode): Normalised[] {
  const Host = defineComponent({
    setup() {
      provide(STATIC_CANVAS, true)
      return () => h('div', { class: 'parity-host' }, [h(CanvasChildren, { node: root })])
    },
  })

  const { container } = render(Host, { global: { plugins: [i18n] } })
  const host = container.querySelector('.parity-host')!
  return [...host.children]
    .map((e) => normalise(e, (el) => declarations(el.getAttribute('style') ?? '')))
    .filter((n): n is Normalised => n !== null)
}

describe('the canvas and the published page draw the same markup', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    // Nothing on the canvas may reach the network here; pages and components lists come back empty.
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"pages":[],"components":[],"permissions":{}}', { status: 200 })))
  })

  afterEach(() => vi.unstubAllGlobals())

  it.each(jsonFiles(join(pageExamples, 'valid')))('%s', (file) => {
    const document = readJson(join(pageExamples, 'valid', file)) as { root: ContentNode }
    const published = readFileSync(join(golden, file.replace(/\.json$/, '.html')), 'utf8')

    expect(canvasTree(document.root)).toEqual(publishedTree(published))
  })

  // 066 FR-010 (analysis C1): the canvas draws the same border markup whatever the preview
  // width, since a preview width only resizes the frame; and no border rule sits inside a
  // container query, so a border looks the same on desktop, tablet and phone. jsdom lays nothing
  // out, so the column fit is checked in a browser (tests/e2e/specs/block-borders.spec.ts).
  it('draws borders the same at every preview width', () => {
    const document = readJson(join(pageExamples, 'valid', 'borders-every-block.json')) as { root: ContentNode }
    const published = publishedTree(readFileSync(join(golden, 'borders-every-block.html'), 'utf8'))

    for (const width of ['1280px', '767px', '480px']) {
      const tree = canvasTree(document.root)
      expect(tree, width).toEqual(published)
    }

    const css = readFileSync(resolve(process.cwd(), 'src/editor/blocks/blocks.css'), 'utf8')
    const queries = [...css.matchAll(/@container[^{]*\{([\s\S]*?)\n {2}\}/g)].map((m) => m[1]!)
    expect(queries.length).toBeGreaterThan(0)
    for (const body of queries) expect(body).not.toMatch(/\.qb-border/)
    expect(css).toMatch(/\.qb-border \{\s*box-sizing: border-box;/)
  })
})
