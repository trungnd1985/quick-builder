import { describe, expect, it } from 'vitest'
import { validateDocument } from '../../src/editor/schema/documentValidator'
import { contentDocument } from '../../src/editor/schema/bindingDocumentRules'
import type { ContentNode } from '../../src/editor/schema/schema'

// 017 research SE-02, page-schema.md §3 rules 22–25: search metadata in documents. The shared
// golden files run in documentValidator.spec.ts as page documents; these are the cases that need
// a component document, or say what the files cannot. SeoSchemaGoldenTests is the server's twin.

const doc = (props: Record<string, unknown>, bindingVersion?: number) => ({
  schemaVersion: 1,
  ...(bindingVersion ? { bindingVersion } : {}),
  root: { id: 'root', type: 'root', props, children: [] },
})

const failure = (result: ReturnType<typeof validateDocument>) =>
  result.valid ? null : { rule: result.rule, pointer: result.pointer }

describe('search metadata in documents', () => {
  it('refuses metadata in a component document (rule 22)', () => {
    expect(failure(validateDocument(doc({ seo: { title: 'Header' } }), 'component'))).toEqual({
      rule: 'seo_in_component',
      pointer: '/root/props/seo',
    })
  })

  it('accepts metadata in a page document', () => {
    expect(validateDocument(doc({ seo: { title: 'Header' } }), 'page')).toEqual({ valid: true })
  })

  it('keeps metadata bindings on an ordinary page (rule 23)', () => {
    const seo = {
      bind: { title: { value: { field: 'f1' } } },
      structuredData: { type: 'Event', bind: { startDate: { value: { field: 'f3' } } } },
    }
    expect(validateDocument(doc({ seo }, 1), 'page')).toEqual({ valid: true })
  })

  it('needs the binding version for a structured data mapping alone (rule 13)', () => {
    const seo = { structuredData: { type: 'Article', bind: { headline: { value: { field: 'f1' } } } } }
    expect(failure(validateDocument(doc({ seo }), 'page'))).toEqual({ rule: 'binding_version_required', pointer: '' })
  })

  it('needs no binding version for a structured data type without bindings', () => {
    expect(validateDocument(doc({ seo: { structuredData: { type: 'Article' } } }), 'page')).toEqual({ valid: true })
  })

  it('refuses a metadata binding naming two values', () => {
    const seo = { bind: { title: { value: { field: 'f1', item: 'slug' } } } }
    expect(failure(validateDocument(doc({ seo }, 1), 'page'))).toEqual({
      rule: 'props_invalid',
      pointer: '/root/props/seo/bind/title/value',
    })
  })

  it('refuses a list scope in metadata: none is open at the root', () => {
    const seo = { bind: { title: { value: { field: 'f1', scope: 'posts' } } } }
    expect(failure(validateDocument(doc({ seo }, 1), 'page'))).toEqual({
      rule: 'unknown_scope',
      pointer: '/root/props/seo/bind/title/value/scope',
    })
  })

  it('refuses an unknown structured data type', () => {
    expect(failure(validateDocument(doc({ seo: { structuredData: { type: 'Recipe' } } }), 'page'))).toEqual({
      rule: 'props_invalid',
      pointer: '/root/props/seo/structuredData/type',
    })
  })

  it('stamps the binding version on a document whose only bindings are metadata', () => {
    const root = {
      id: 'root',
      type: 'root',
      props: { seo: { bind: { description: { value: { field: 'f2' } } } } },
      children: [],
    } as unknown as ContentNode

    const saved = contentDocument(root)

    expect(saved.bindingVersion).toBe(1)
    expect(validateDocument(saved, 'page')).toEqual({ valid: true })
  })
})
