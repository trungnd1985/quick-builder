import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { checkField, VALUE_CODES } from '../../src/editor/forms/formRules'
import type { JsonObject } from '../../src/editor/schema/subsetValidator'

// Submitted values against a field's rules (018 research FS-08), from the fixture the server's
// FormValueRulesTests and the published form script run too.
interface ValueCase {
  name: string
  field: JsonObject
  values: string[]
  expect: { code: string | null; values: string[] }
}

const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../tests/fixtures/forms/value-rules.json'), 'utf8')) as { cases: ValueCase[] }

/** The fixture's ${Na}: the letter a, N times. */
const expand = (value: string) => value.replace(/\$\{(\d+)a\}/g, (_, n: string) => 'a'.repeat(Number(n)))

describe('form value rules against the shared fixture', () => {
  it.each(fixture.cases.map((c) => [c.name, c] as const))('%s', (_name, testCase) => {
    const result = checkField(testCase.field, testCase.values.map(expand))

    expect(result).toEqual({ code: testCase.expect.code, values: testCase.expect.values.map(expand) })
  })

  it('exercises every value code of the contract', () => {
    const exercised = new Set(fixture.cases.map((c) => c.expect.code).filter((c): c is string => c !== null))

    expect([...exercised].sort()).toEqual([...VALUE_CODES].sort())
  })
})
