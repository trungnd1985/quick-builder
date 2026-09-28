import { describe, expect, it } from 'vitest'
import { join } from 'node:path'
import { validateTokenSet } from '../../src/editor/tokens/tokenValidator'
import { contractsDir, expectedFailures, jsonFiles, readJson } from '../editor/contractFiles'

// 011 research ED-14, SC-009: the admin's check of a token set gives the server's answer for
// every golden file of the design token contract — same code, same pointer.

const examples = join(contractsDir, 'design-tokens', 'examples')

describe('design token validator against the golden files', () => {
  it.each(jsonFiles(join(examples, 'valid')))('accepts valid/%s', (file) => {
    expect(validateTokenSet(readJson(join(examples, 'valid', file)))).toEqual({ valid: true })
  })

  it.each(Object.entries(expectedFailures(join(examples, 'invalid'))))('refuses invalid/%s', (file, { code, pointer }) => {
    const result = validateTokenSet(readJson(join(examples, 'invalid', file)))

    expect(result).toMatchObject({ valid: false, code, pointer })
    expect(file.startsWith(code!)).toBe(true)
  })
})
