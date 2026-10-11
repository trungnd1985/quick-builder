import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { filterProblem, operatorsFor, literalFor } from '../../src/editor/bindings/listRules'
import { parseSchema } from '../../src/editor/bindings/bindingRules'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { COLLECTION, SITE, documentWith, mountEditor, stubServer, type Server } from './harness'

// 016 US4 (tasks T063, T069): the list's source, filter, sort and paging controls offer only
// what fits each field's type (binding-expression.md §5), and mark a filter that no longer fits.

const schema = {
  schemaVersion: 1,
  titleFieldId: 'f1',
  fields: [
    { id: 'f1', key: 'title', name: 'Title', help: '', required: true, type: 'text', multiline: false, maxLength: 200 },
    { id: 'f2', key: 'body', name: 'Body', help: '', required: false, type: 'richText' },
    { id: 'f3', key: 'reading-time', name: 'Reading time', help: '', required: false, type: 'number', decimals: 0 },
    { id: 'f4', key: 'featured', name: 'Featured', help: '', required: false, type: 'boolean' },
    { id: 'f8', key: 'category', name: 'Category', help: '', required: false, type: 'option', options: [{ id: 'o1', label: 'News' }, { id: 'o2', label: 'Guides' }] },
  ],
}

const listDocument = (props: object = {}) => ({
  ...documentWith({
    id: 's1', type: 'section', props: {}, children: [{
      id: 'list', type: 'collection-list', props: { source: { collectionId: COLLECTION }, ...props }, children: [
        { id: 'card', type: 'list-card', props: {}, children: [] },
        { id: 'empty', type: 'list-empty', props: {}, children: [] },
      ],
    }],
  }),
  bindingVersion: 1,
})

function serveSchema(server: Server): void {
  for (let i = 0; i < 5; i++) {
    server.script(`GET /api/v1/cms/sites/${SITE}/collections/${COLLECTION}`, { status: 200, body: { id: COLLECTION, name: 'Posts', slug: 'posts', schema } })
  }
}

const panel = () => screen.getByRole('region', { name: en.editor.panel.title })
const options = (select: HTMLSelectElement) => [...select.options].map((o) => o.textContent!.trim())

async function openList(props: object = {}) {
  const server = stubServer(listDocument(props))
  serveSchema(server)
  await mountEditor()
  const store = useEditorStore()
  store.select('list')
  await waitFor(() => expect(within(panel()).getByText(en.editor.panel.props.filters)).toBeTruthy())
  return { server, store }
}

describe('the operator table (binding-expression.md §5)', () => {
  const info = parseSchema(COLLECTION, schema as never)
  const field = (id: string) => ({ field: info.fields[id] })

  it('offers each type only its operators', () => {
    expect(operatorsFor(field('f1'))).toEqual(['eq', 'ne', 'contains', 'empty', 'notEmpty'])
    expect(operatorsFor(field('f3'))).toEqual(['eq', 'ne', 'gt', 'lt', 'empty', 'notEmpty'])
    expect(operatorsFor(field('f4'))).toEqual(['eq'])
    expect(operatorsFor(field('f2'))).toEqual(['empty', 'notEmpty'])
    expect(operatorsFor({ item: 'slug' })).toEqual(['eq', 'ne'])
    expect(literalFor({ item: 'firstPublishedAt' }, 'gt')).toBe('date')
  })

  it('says why a stored filter no longer fits', () => {
    expect(filterProblem({ field: 'f99', op: 'eq', value: 'x' }, info)).toBe('field_missing')
    expect(filterProblem({ field: 'f4', op: 'contains', value: true }, info)).toBe('operator')
    expect(filterProblem({ field: 'f8', op: 'eq', value: 'o9' }, info)).toBe('value')
    expect(filterProblem({ field: 'f3', op: 'gt', value: 5 }, info)).toBeNull()
    expect(filterProblem({ field: 'f1', op: 'eq', value: { field: 'f1', scope: 'page' } }, info)).toBeNull()
    expect(filterProblem({ field: 'f2', op: 'empty' }, info)).toBeNull()
  })
})

describe('the list controls', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('chooses the source among the site’s collections', async () => {
    await openList()

    const source = within(panel()).getByLabelText(en.editor.panel.props.source) as HTMLSelectElement
    await waitFor(() => expect(options(source)).toContain('Posts'))
    expect(source.value).toBe(`collection:${COLLECTION}`)
  })

  it('adds a filter with operators and a value input that fit its field', async () => {
    const { store } = await openList()
    await waitFor(() => expect((within(panel()).getByRole('button', { name: en.editor.list.addFilter }) as HTMLButtonElement).disabled).toBe(false))

    await fireEvent.click(within(panel()).getByRole('button', { name: en.editor.list.addFilter }))
    await waitFor(() => expect(store.root!.children[0].children[0].props.filters).toEqual([{ field: 'f1', op: 'eq', value: '' }]))

    const row = within(panel()).getByRole('group', { name: en.editor.list.filterN.replace('{n}', '1') })
    const operator = within(row).getByLabelText(en.editor.list.filterOperator) as HTMLSelectElement
    expect(options(operator)).toEqual(['eq', 'ne', 'contains', 'empty', 'notEmpty'].map((op) => en.editor.list.operators[op as 'eq']))

    // A yes/no field: one operator, and a Yes/No value.
    const target = within(row).getByLabelText(en.editor.list.filterTarget) as HTMLSelectElement
    await fireEvent.update(target, 'field:f4')
    await fireEvent.change(target)
    await waitFor(() => expect(store.root!.children[0].children[0].props.filters).toEqual([{ field: 'f4', op: 'eq', value: true }]))
    const row2 = within(panel()).getByRole('group', { name: en.editor.list.filterN.replace('{n}', '1') })
    expect(options(within(row2).getByLabelText(en.editor.list.filterOperator) as HTMLSelectElement)).toEqual([en.editor.list.operators.eq])
    expect(options(within(row2).getByLabelText(en.editor.list.filterValue) as HTMLSelectElement)).toEqual([en.editor.list.yes, en.editor.list.no])
  })

  it('marks a filter whose field no longer exists', async () => {
    await openList({ filters: [{ field: 'f99', op: 'eq', value: 'x' }] })

    await waitFor(() => expect(within(panel()).getByText(en.editor.list.filterBroken.field_missing)).toBeTruthy())
  })

  it('sorts only on fields that order', async () => {
    const { store } = await openList()

    await fireEvent.click(within(panel()).getByRole('button', { name: en.editor.list.addSort }))
    await waitFor(() => expect(store.root!.children[0].children[0].props.sort).toEqual([{ item: 'firstPublishedAt', dir: 'desc' }]))

    const row = within(panel()).getByRole('group', { name: en.editor.list.sortN.replace('{n}', '1') })
    const target = within(row).getByLabelText(en.editor.list.sortTarget) as HTMLSelectElement
    await waitFor(() => expect(options(target)).toContain('Reading time'))
    expect(options(target)).not.toContain('Body')
  })

  it('splits the list into pages', async () => {
    const { store } = await openList()

    await fireEvent.click(within(panel()).getByLabelText(en.editor.list.paging.on))
    await waitFor(() => expect(store.root!.children[0].children[0].props.paging).toEqual({ pageSize: 12 }))
  })

  it('passes axe checks', async () => {
    await openList({ filters: [{ field: 'f3', op: 'gt', value: 5 }], sort: [{ field: 'f1', dir: 'asc' }] })
    await waitFor(() => expect(within(panel()).getByRole('group', { name: en.editor.list.filterN.replace('{n}', '1') })).toBeTruthy())

    const results = await axe.run(panel(), { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(', ')}`)).toEqual([])
  })
})
