import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { toContractRichText } from '../../src/editor/richtext/toContractRichText'
import { pageExamples } from './contractFiles'

// 035 SC-006, research RT-15: every change in a rich text runs the whole document through
// toContractRichText. At the limits — 50 images, 20 tables, one of 200 rows × 20 columns,
// 20,000 characters — that stays well inside a frame, so typing never waits on it.

const BUDGET_MS = 30

describe('rich text at the limits', () => {
  it(`normalises within ${BUDGET_MS} ms`, () => {
    const page = JSON.parse(readFileSync(join(pageExamples, 'valid', 'rich-text-limits.json'), 'utf8'))
    const content = page.root.children[0].children[0].props.content
    toContractRichText(content)

    const runs = 20
    const started = performance.now()
    for (let i = 0; i < runs; i++) toContractRichText(content)
    const each = (performance.now() - started) / runs

    console.info(`rich-text-limits: ${each.toFixed(2)} ms per normalisation`)
    expect(each).toBeLessThan(BUDGET_MS)
  })
})
