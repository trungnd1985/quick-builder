import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import { readdirSync } from 'node:fs'
import { contractsDir, readJson } from '../contractFiles'
import { parseSchema } from '../../../src/editor/bindings/bindingRules'
import { resolveBinding, type Bound, type BoundItem, type OpenScope, type ResolveData } from '../../../src/editor/bindings/resolve'
import type { ContentNode } from '../../../src/editor/schema/schema'
import type { Json } from '../../../src/editor/schema/subsetValidator'

// 016 research TB-05, TB-09 (tasks T019): every resolve case of the binding contract gives
// exactly its expected outcome in the editor. BindingRulesGoldenTests runs the same cases on
// the server, so the canvas and the published page agree on every bound value.

const casesDir = join(contractsDir, 'binding-expression', 'examples', 'resolve')

interface CaseFile {
  language: string
  basePath: string
  schemas: Record<string, Json>
  pageItem: Omit<BoundItem, never> | null
  items: BoundItem[]
  templates: Record<string, string>
  media: Record<string, string>
  scopes: { name: string; item: string }[]
  document: { root: ContentNode }
  expected: Record<string, Record<string, unknown>>
}

const cases = readdirSync(casesDir).filter((f) => f.endsWith('.json')).sort()

function load(file: string): { data: ResolveData; scopes: OpenScope[]; root: ContentNode; expected: CaseFile['expected'] } {
  const raw = readJson(join(casesDir, file)) as CaseFile
  const items = Object.fromEntries(raw.items.map((i) => [i.id, i]))
  const data: ResolveData = {
    pageItem: raw.pageItem,
    items,
    schemas: Object.fromEntries(Object.entries(raw.schemas).map(([id, schema]) => [id, parseSchema(id, schema)])),
    templates: raw.templates,
    media: raw.media,
    language: raw.language,
    basePath: raw.basePath,
  }
  const scopes = raw.scopes.map((s) => ({ name: s.name, item: items[s.item] }))
  return { data, scopes, root: raw.document.root, expected: raw.expected }
}

function find(node: ContentNode, id: string): ContentNode | undefined {
  if (node.id === id) return node
  for (const child of node.children) {
    const found = find(child, id)
    if (found) return found
  }
  return undefined
}

/** A bound value in the shape the case files write outcomes in. */
function describeBound(bound: Bound): Record<string, unknown> {
  let outcome: Record<string, unknown>
  if (bound.state === 'value') {
    if (bound.text !== undefined) outcome = { text: bound.text }
    else if (bound.richText !== undefined) outcome = { richText: bound.richText }
    else if (bound.media !== undefined) outcome = { media: bound.media }
    else if (bound.flag) outcome = { flag: true }
    else outcome = { link: bound.link!.href }
  } else if (bound.state === 'hide') outcome = { hide: true }
  else outcome = { fallback: true }
  if ('broken' in bound && bound.broken) outcome.broken = bound.broken
  return outcome
}

const outcomes = cases.flatMap((file) => Object.keys(load(file).expected).map((key) => [file, key] as const))

describe('binding resolution against the contract cases', () => {
  it('has cases with outcomes', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const file of cases) expect(Object.keys(load(file).expected).length).toBeGreaterThan(0)
  })

  it.each(outcomes)('%s: %s', (file, key) => {
    const { data, scopes, root, expected } = load(file)
    // A page's metadata is `root.seo.<field>`: the node id is everything before the first dot.
    const dot = key.indexOf('.')
    const node = find(root, key.slice(0, dot))!

    expect(describeBound(resolveBinding(node, key.slice(dot + 1), data, scopes))).toEqual(expected[key])
  })
})
