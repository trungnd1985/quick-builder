import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { formLinkedPages, formSetupProblems } from '../../src/editor/forms/formSetup'
import type { JsonObject } from '../../src/editor/schema/subsetValidator'

// The publish rules of a form (page-schema.md §3 rule 28; 018 research FS-04), from the fixture
// the server's FormPublishRulesTests runs too, so the panel shows what publishing refuses.
interface SetupCase {
  name: string
  props: JsonObject
  problems: { rule: string; fieldId: string | null }[]
}

const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../tests/fixtures/forms/setup-rules.json'), 'utf8')) as {
  cases: SetupCase[]
}

describe('form publish rules against the shared fixture', () => {
  it.each(fixture.cases.map((c) => [c.name, c] as const))('%s', (_name, testCase) => {
    expect(formSetupProblems(testCase.props)).toEqual(testCase.problems)
  })
})

describe('pages a form needs live', () => {
  it('lists the redirect page first, then each consent link once', () => {
    const props = {
      name: 'F',
      fields: [
        { id: 'a00000000001', type: 'consent', label: 'A', statement: 'S', linkPageId: 'p1' },
        { id: 'a00000000002', type: 'consent', label: 'B', statement: 'S', linkPageId: 'p1' },
      ],
      redirect: { type: 'page', pageId: 'p2' },
    }

    expect(formLinkedPages(props)).toEqual(['p2', 'p1'])
  })

  it('needs no page for a redirect to an address', () => {
    expect(formLinkedPages({ name: 'F', fields: [], redirect: { type: 'url', href: 'https://example.com/' } })).toEqual([])
  })
})
