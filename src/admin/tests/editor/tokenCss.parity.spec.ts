import { describe, expect, it } from 'vitest'
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { tokenCss } from '../../src/editor/tokens/tokenCss'
import type { TokenSet } from '../../src/editor/tokens/defaultTokens'

// 012 research PR-10: the renderer's TokenCss (C#) ports this file's tokenCss. Both run the same
// fixtures, so the canvas and a published page get the same custom properties from a design.
// This implementation is the reference: QB_UPDATE_GOLDEN=1 writes each fixture's `expected`
// from it; TokenCssParityTests then holds the C# port to the same files.

const fixtures = resolve(process.cwd(), '../../tests/fixtures/rendering/token-css')
const files = readdirSync(fixtures).filter((name) => name.endsWith('.json')).sort()
const update = process.env.QB_UPDATE_GOLDEN === '1'

describe('token CSS against the shared fixtures', () => {
  it('has fixtures to run', () => {
    expect(files.length).toBeGreaterThanOrEqual(3)
  })

  it.each(files)('%s', (file) => {
    const path = join(fixtures, file)
    const fixture = JSON.parse(readFileSync(path, 'utf8')) as { tokens: TokenSet; expected: Record<string, string> }
    const actual = tokenCss(fixture.tokens)

    if (update) {
      writeFileSync(path, `${JSON.stringify({ ...fixture, expected: actual }, null, 2)}\n`)
      return
    }

    // Order matters too: it is the order the properties appear in the published style block.
    expect(Object.entries(actual)).toEqual(Object.entries(fixture.expected))
  })
})
