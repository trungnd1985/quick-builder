import { describe, expect, it } from 'vitest'
import { parseSchema } from '../../../src/editor/bindings/bindingRules'
import { fieldSources, operandChoices, scopesAt } from '../../../src/editor/bindings/scopes'
import { previewRequest } from '../../../src/editor/bindings/listPreview'
import type { ContentNode } from '../../../src/editor/schema/schema'

// 016 US6 (tasks T077, T080): the scopes a node can read, nearest first and named by their list's
// label; the values a filter can take from them; and the multi-reference fields a list can show.

const POSTS = '0192f5a0-0000-7000-8000-00000000c001'
const PEOPLE = '0192f5a0-0000-7000-8000-00000000c002'

const schemas = {
  [POSTS]: parseSchema(POSTS, {
    schemaVersion: 1,
    fields: [
      { id: 'f1', key: 'title', name: 'Title', type: 'text' },
      { id: 'f8', key: 'category', name: 'Category', type: 'option', options: [{ id: 'o1', label: 'News' }] },
      { id: 'f9', key: 'author', name: 'Author', type: 'reference', target: PEOPLE },
      { id: 'f10', key: 'related', name: 'Related', type: 'multiReference', target: PEOPLE },
    ],
  }),
  [PEOPLE]: parseSchema(PEOPLE, { schemaVersion: 1, fields: [{ id: 'f1', key: 'name', name: 'Name', type: 'text' }] }),
}

const node = (id: string, type: string, props: Record<string, unknown> = {}, children: ContentNode[] = []): ContentNode =>
  ({ id, type, props, children }) as ContentNode

// A template of posts holding a list of posts ("More"), whose card holds a list of people.
const inner = node('people', 'collection-list', { source: { collectionId: PEOPLE } }, [node('people-card', 'list-card'), node('people-empty', 'list-empty')])
const outer = node('more', 'collection-list', { source: { collectionId: POSTS }, layerLabel: 'More' }, [
  node('more-card', 'list-card', {}, [inner, node('inside', 'heading')]),
  node('more-empty', 'list-empty', {}, [node('empty-heading', 'heading')]),
])
const root = node('root', 'root', {}, [node('s1', 'section', {}, [outer])])

describe('scopes on a template page', () => {
  it('offers each depth its scopes, nearest first, named by the list label', () => {
    expect(scopesAt(root, 'inside', POSTS, schemas).map((s) => [s.name, s.label])).toEqual([['more', 'More'], ['page', null]])
    expect(scopesAt(root, 'people-card', POSTS, schemas).map((s) => s.name)).toEqual(['people', 'more', 'page'])
    // The empty state has no item: only the page's.
    expect(scopesAt(root, 'empty-heading', POSTS, schemas).map((s) => s.name)).toEqual(['page'])
  })

  it('offers filter values of the same type from every enclosing scope', () => {
    const titles = operandChoices(root, inner, POSTS, schemas, { type: 'text' }, 'eq')
    expect(titles.map((c) => c.value)).toEqual([
      { field: 'f1', scope: 'more' },
      { field: 'f1', scope: 'page' },
    ])

    // A person filter can take an author of the card's post or the page's post.
    const people = operandChoices(root, inner, POSTS, schemas, { type: 'reference', referenceTarget: PEOPLE }, 'eq')
    expect(people.map((c) => c.value)).toEqual([{ field: 'f9', scope: 'more' }, { field: 'f9', scope: 'page' }])
  })

  it('offers "leave out that item" only for a list of an enclosing scope’s collection', () => {
    expect(operandChoices(root, outer, POSTS, schemas, { type: 'slug' }, 'ne').map((c) => c.value)).toEqual([{ current: true, scope: 'page' }])
    expect(operandChoices(root, inner, POSTS, schemas, { type: 'slug' }, 'ne')).toEqual([])
    expect(operandChoices(root, outer, POSTS, schemas, { type: 'slug' }, 'eq')).toEqual([])
  })

  it('offers the multi-reference fields of every enclosing scope as list sources', () => {
    expect(fieldSources(root, inner, POSTS, schemas).map((s) => [s.scope.name, s.fieldId])).toEqual([['more', 'f10'], ['page', 'f10']])
    expect(fieldSources(root, outer, null, schemas)).toEqual([])
  })
})

describe('the preview request', () => {
  it('names the sample item and the item of each card the list sits in', () => {
    const card = { name: 'more', item: { id: 'item-1', collectionId: POSTS, slug: 'a', name: 'A', firstPublishedAt: '', lastPublishedAt: '', values: {} } }

    expect(previewRequest(inner, 'sample-1', [card])).toEqual({
      list: { source: { collectionId: PEOPLE } },
      scopes: { page: 'sample-1', more: 'item-1' },
    })
    expect(previewRequest(node('x', 'collection-list', { source: {} }), null)).toBeNull()
  })
})
