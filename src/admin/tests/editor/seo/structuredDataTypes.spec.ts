import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import { readdirSync, readFileSync } from 'node:fs'
import { contractsDir, readJson } from '../contractFiles'
import { parseSchema } from '../../../src/editor/bindings/bindingRules'
import { STRUCTURED_DATA_TYPES, structuredBroken } from '../../../src/editor/seo/structuredDataTypes'
import type { Json } from '../../../src/editor/schema/subsetValidator'

// 017 research SE-19 (tasks T093): the editor's types are the tables of structured-data.md §3,
// and it marks exactly the broken bindings of the contract's map/ examples, as the renderer does
// (StructuredDataTypesContractTests, StructuredDataGoldenTests).

interface Row { name: string; required: boolean; accepts: string[]; default: string | null }

function documented(): Record<string, Row[]> {
  const types: Record<string, Row[]> = {}
  let current: Row[] | null = null
  for (const line of readFileSync(join(contractsDir, 'structured-data.md'), 'utf8').split(/\r?\n/)) {
    const heading = /^### `([A-Za-z]+)`$/.exec(line)
    if (heading) {
      current = types[heading[1]] = []
      continue
    }
    if (line.startsWith('## 4')) break
    const row = /^\| `([A-Za-z]+)`( \(required\))? \| ([^|]+) \| ([^|]+) \|/.exec(line)
    if (!current || !row) continue
    const cell = row[3].trim()
    const accepts = cell.startsWith('as `')
      ? Object.values(types).flat().find((r) => r.name === cell.slice(4, -1))!.accepts
      : cell.split(',').map((part) => {
        const p = part.trim().replace(/`/g, '')
        if (p === 'rich text') return 'richText'
        if (p === 'text template') return 'template'
        return p.startsWith('reference') ? 'reference' : p
      })
    const fallback = row[4].trim()
    current.push({
      name: row[1],
      required: row[2] !== undefined,
      accepts,
      default: fallback === '—' ? null : fallback === "the item's name" ? 'name' : fallback.replace(/`/g, ''),
    })
  }
  return types
}

describe('structured data types', () => {
  it('match the contract tables', () => {
    const tables = documented()
    expect(Object.keys(STRUCTURED_DATA_TYPES).sort()).toEqual(Object.keys(tables).sort())
    for (const [name, rows] of Object.entries(tables)) {
      const loaded = STRUCTURED_DATA_TYPES[name].properties
      expect(loaded.map((p) => p.name)).toEqual(rows.map((r) => r.name))
      for (const row of rows) {
        const property = loaded.find((p) => p.name === row.name)!
        expect([property.required, property.default, [...property.accepts].sort()], `${name}.${row.name}`)
          .toEqual([row.required, row.default, [...row.accepts].sort()])
      }
    }
  })

  const mapDir = join(contractsDir, 'structured-data', 'examples', 'map')
  const cases = readdirSync(mapDir).filter((f) => f.endsWith('.json')).sort()

  it.each(cases)('marks the broken bindings of %s', (file) => {
    const raw = readJson(join(mapDir, file)) as {
      collectionId: string
      schemas: Record<string, Json>
      structuredData: { type: string; bind: Json }
      broken: Record<string, string>
    }
    const schemas = Object.fromEntries(Object.entries(raw.schemas).map(([id, schema]) => [id, parseSchema(id, schema)]))

    expect(structuredBroken(raw.structuredData.type, raw.structuredData.bind, schemas[raw.collectionId], schemas)).toEqual(raw.broken)
  })
})
