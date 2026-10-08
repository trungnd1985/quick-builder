import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import type { SchemaDocument } from '../../../src/api/cms'
import type { Problem } from '../../../src/api/items'
import { checkItem } from '../../../src/features/cms/items/itemValueRules'
import { contractsDir, readJson } from '../../editor/contractFiles'

// 015 FR-012: the form's checks give exactly the server's report for every golden item-values
// example. ItemValueGoldenTests runs the same files on the server. Neither side knows a media
// file or an item here, so media and references are always reported missing.

const directory = join(contractsDir, 'collection-schema', 'examples', 'items')
const schema = readJson(join(directory, 'schema.json')) as unknown as SchemaDocument
const expected = readJson(join(directory, 'expected.json')) as unknown as Record<string, { problems: Problem[] }>

const knowsNothing = { mediaKind: () => 'missing' as const, collectionOf: () => null }

describe('item values against the golden files', () => {
  it.each(Object.entries(expected))('%s gives its expected report', (file, want) => {
    const document = readJson(join(directory, file)) as unknown as { values: Record<string, unknown> }

    const problems = checkItem(schema, 'example', document.values, 'self', knowsNothing)

    expect(problems.map(({ fieldId, problem, limit }) => (limit === undefined || limit === null ? { fieldId, problem } : { fieldId, problem, limit }))).toEqual(
      want.problems,
    )
  })
})

describe('slugs', () => {
  it('reports a slug that breaks the grammar', () => {
    expect(checkItem(schema, 'Not A Slug', { f1: 'Hello' })).toEqual([{ fieldId: 'slug', problem: 'invalid_slug', limit: 100 }])
  })
})
