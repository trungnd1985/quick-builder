import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import { readFileSync } from 'node:fs'
import { validateSchema, suggestKey, TemporaryIds } from '../../src/features/cms/collectionSchema'
import { normalise } from '../../src/features/cms/collectionSchemaRules'
import { contractsDir, expectedFailures, jsonFiles, readJson } from '../editor/contractFiles'

// 014 SC-006: the builder's validator gives exactly the server's answer for every golden file
// of the collection schema — same code, rule and pointer. CollectionSchemaGoldenTests runs the
// same files on the server.

const examples = join(contractsDir, 'collection-schema', 'examples')

// Files that are not JSON, or whose defect JSON.parse erases, are the server's alone to judge.
const SERVER_ONLY = new Set(['malformed_json--truncated.json', 'duplicate_property--field-key.json'])

describe('collection schema validator against the golden files', () => {
  it.each(jsonFiles(join(examples, 'valid')))('accepts valid/%s', (file) => {
    expect(validateSchema(readJson(join(examples, 'valid', file)), 'stored').valid).toBe(true)
  })

  const invalid = Object.entries(expectedFailures(join(examples, 'invalid'))).filter(([file]) => !SERVER_ONLY.has(file))

  it.each(invalid)('refuses invalid/%s with its code, rule and pointer', (file, expected) => {
    const result = validateSchema(JSON.parse(readFileSync(join(examples, 'invalid', file), 'utf8')), 'stored')

    expect(result.valid).toBe(false)
    if (!result.valid) {
      expect({ code: result.code, rule: result.rule, pointer: result.pointer }).toEqual(expected)
    }
  })
})

describe('proposals', () => {
  it('names the items after the first single-line text field and makes it required', () => {
    const document = normalise({
      schemaVersion: 1,
      fields: [
        { id: '~1', key: 'price', name: 'Price', help: '', required: false, type: 'number', decimals: 2 },
        { id: '~2', key: 'headline', name: 'Headline', help: '', required: false, type: 'text', multiline: false, maxLength: 256 },
      ],
    })

    expect(document.titleFieldId).toBe('~2')
    expect(document.fields[1]?.required).toBe(true)
    expect(validateSchema(document, 'proposal').valid).toBe(true)
  })

  it('accepts temporary ids in a proposal only', () => {
    const document = { schemaVersion: 1, fields: [{ id: '~1', key: 'flag', name: 'Flag', help: '', required: false, type: 'boolean' as const }] }

    expect(validateSchema(document, 'proposal').valid).toBe(true)
    expect(validateSchema(document, 'stored')).toMatchObject({ valid: false, rule: 'temporary_id_in_stored' })
  })

  it('suggests keys in the key grammar and allocates fresh temporary ids', () => {
    expect(suggestKey('Reading time (min)')).toBe('reading-time-min')
    expect(suggestKey('Ảnh bìa')).toBe('anh-bia')
    expect(suggestKey('2026 plans')).toBe('plans')

    const ids = new TemporaryIds({ schemaVersion: 1, fields: [{ id: '~4', key: 'a', name: 'A', help: '', required: false, type: 'boolean' }] })
    expect([ids.take(), ids.take()]).toEqual(['~5', '~6'])
  })
})
