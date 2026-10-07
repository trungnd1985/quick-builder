import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import CollectionBuilderView from '../../src/features/cms/CollectionBuilderView.vue'
import type { Collection, Preview, SchemaDocument } from '../../src/api/cms'
import { blogSchema, collection, COLLECTION, en, i18n, problem, SITE, stubFetch, violations, type Call, type Reply } from './harness'

// 014 US1–US4, US6: the schema builder — fields edited, added, reordered by keyboard, checked
// before saving; free changes saved directly, others previewed and confirmed with a digest;
// conflicts and read-only access.

const base = `/api/v1/cms/sites/${SITE}/collections`

const preview = (extra: Partial<Preview> = {}): Preview => ({
  revision: 2,
  itemCount: 300,
  requiresConfirmation: true,
  digest: 'a'.repeat(64),
  changes: [{
    kind: 'fieldRemoved', rule: 'lossy', fieldId: 'f3', key: 'reading-time', name: 'Reading time', setting: null, optionId: null,
    from: null, to: null, counts: { cleared: 212 }, examples: [{ itemId: 'i1', title: 'Hello world', outcome: 'cleared' }], fillInAllowed: false,
  }],
  ...extra,
})

function server(loaded: Collection, overrides: (call: Call) => Reply | undefined = () => undefined) {
  return (call: Call): Reply => {
    const scripted = overrides(call)
    if (scripted) return scripted
    if (call.url === base) return { status: 200, body: { collections: [{ id: COLLECTION, name: 'Blog posts', slug: 'blog-posts', fieldCount: 3, itemCount: 300, revision: 2, updatedAtUtc: loaded.updatedAtUtc }], canManage: loaded.canManage } }
    if (call.url.endsWith('/history')) return { status: 200, body: { entries: [], nextBefore: null } }
    if (call.method === 'PUT') {
      const body = call.body as { schema: SchemaDocument; name: string }
      const idMap: Record<string, string> = {}
      let next = 10
      const schema = {
        ...body.schema,
        fields: body.schema.fields.map((f) => {
          if (!f.id.startsWith('~')) return f
          idMap[f.id] = `f${next}`
          return { ...f, id: `f${next++}` }
        }),
      }
      return { status: 200, body: collection(schema, { revision: loaded.revision + 1, name: body.name, idMap }) }
    }
    return { status: 200, body: loaded }
  }
}

async function renderView(reply: (call: Call) => Reply) {
  const calls = stubFetch(reply)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/collections', name: 'site-collections', component: { template: '<div>list</div>' } },
      { path: '/sites/:siteId/collections/:collectionId', name: 'site-collection', component: CollectionBuilderView },
      { path: '/sites/:siteId/collections/:collectionId/items', name: 'site-collection-items', component: { template: '<div>items</div>' } },
    ],
  })
  await router.push(`/sites/${SITE}/collections/${COLLECTION}`)
  await router.isReady()

  const result = render(CollectionBuilderView, { global: { plugins: [i18n, router] } })
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: 'Blog posts' })).toBeTruthy())
  return { ...result, calls, router }
}

const fieldButton = (name: string, type: string) =>
  screen.getByRole('button', { name: new RegExp(`^${name}, ${type}`) })

describe('CollectionBuilderView', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('renames a field and saves it straight away, without a preview', async () => {
    const { calls, container } = await renderView(server(collection(blogSchema())))
    expect(await violations(container)).toEqual([])

    await fireEvent.click(fieldButton('Summary', 'Text'))
    await fireEvent.update(screen.getByLabelText(en.cms.field.name), 'Excerpt')
    await fireEvent.update(screen.getByLabelText(en.cms.field.key), 'excerpt')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.some((c) => c.url.endsWith('/preview'))).toBe(false)
    const sent = calls.find((c) => c.method === 'PUT')?.body as { revision: number; schema: SchemaDocument }
    expect(sent.revision).toBe(2)
    expect(sent.schema.fields[1]).toMatchObject({ id: 'f2', name: 'Excerpt', key: 'excerpt' })
  })

  it('adds one field of every type with temporary ids, and keeps a key suggested until it is edited', async () => {
    const { calls } = await renderView(server(collection(blogSchema())))
    const types = ['text', 'richText', 'number', 'boolean', 'date', 'image', 'file', 'option', 'reference', 'multiReference']

    for (const type of types) {
      await fireEvent.update(screen.getByLabelText(en.cms.add.type), type)
      await fireEvent.click(screen.getByRole('button', { name: en.cms.add.add }))
    }

    // The last one added is selected: point it at this collection, and give the option field an option.
    await fireEvent.update(screen.getByLabelText(en.cms.field.target), COLLECTION)
    await fireEvent.click(fieldButton(en.cms.add.newName.replace('{type}', en.cms.types.reference), en.cms.types.reference))
    await fireEvent.update(screen.getByLabelText(en.cms.field.target), COLLECTION)
    await fireEvent.click(fieldButton(en.cms.add.newName.replace('{type}', en.cms.types.option), en.cms.types.option))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.options.add }))
    await fireEvent.update(screen.getByLabelText(en.cms.options.label.replace('{n}', '1')), 'News')

    await fireEvent.update(screen.getByLabelText(en.cms.field.name), 'Category')
    expect((screen.getByLabelText(en.cms.field.key) as HTMLInputElement).value).toBe('category')
    await fireEvent.update(screen.getByLabelText(en.cms.field.key), 'kind')
    await fireEvent.update(screen.getByLabelText(en.cms.field.name), 'Kind of post')
    expect((screen.getByLabelText(en.cms.field.key) as HTMLInputElement).value).toBe('kind')

    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))

    const sent = (calls.find((c) => c.method === 'PUT')?.body as { schema: SchemaDocument }).schema
    expect(sent.fields.slice(3).map((f) => f.type)).toEqual(types)
    expect(sent.fields.slice(3).every((f) => f.id.startsWith('~'))).toBe(true)
    expect(sent.fields.find((f) => f.type === 'option')?.options?.[0]?.label).toBe('News')
    await waitFor(() => expect(screen.getByText(en.cms.status.saved.replace('{revision}', '3'))).toBeTruthy())
  })

  it('points at the field to fix before sending anything', async () => {
    const { calls } = await renderView(server(collection(blogSchema())))

    await fireEvent.click(fieldButton('Summary', 'Text'))
    await fireEvent.update(screen.getByLabelText(en.cms.field.key), 'title')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))

    expect(await screen.findByText(en.cms.builder.invalid.replace('{reason}', en.cms.rules.duplicate_key))).toBeTruthy()
    expect(calls.some((c) => c.method !== 'GET')).toBe(false)
  })

  it('reorders fields with Alt and the arrow keys, and says so', async () => {
    const { calls } = await renderView(server(collection(blogSchema())))

    const summary = fieldButton('Summary', 'Text')
    summary.focus()
    await fireEvent.keyDown(summary, { key: 'ArrowUp', altKey: true })

    const items = within(screen.getByRole('list', { name: en.cms.fieldList.label })).getAllByRole('button')
    expect(items[0]?.textContent).toContain('Summary')
    expect(document.activeElement?.textContent).toContain('Summary')
    expect(screen.getByText(en.cms.status.moved.replace('{name}', 'Summary').replace('{position}', '1').replace('{count}', '3'))).toBeTruthy()

    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect((calls.find((c) => c.method === 'PUT')?.body as { schema: SchemaDocument }).schema.fields.map((f) => f.id)).toEqual(['f2', 'f1', 'f3'])
  })

  it('previews a removal, warns that it cannot be undone, and confirms with the digest', async () => {
    const { calls } = await renderView(server(collection(blogSchema()), (call) => (call.url.endsWith('/preview') ? { status: 200, body: preview() } : undefined)))

    await fireEvent.click(fieldButton('Reading time', 'Number'))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.field.remove }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))

    const dialog = await screen.findByRole('alertdialog', { name: en.cms.preview.title })
    expect(within(dialog).getByText(en.cms.preview.outcome.cleared.replace('{count}', '212'))).toBeTruthy()
    expect(within(dialog).getByText(en.cms.preview.undo)).toBeTruthy()
    expect(within(dialog).getByText(/Hello world/)).toBeTruthy()

    await fireEvent.click(within(dialog).getByRole('button', { name: en.cms.preview.confirm }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')?.body?.confirmation).toEqual({ digest: 'a'.repeat(64) })
  })

  it('shows the new preview when the items changed since the first one', async () => {
    const fresh = preview({ digest: 'b'.repeat(64), changes: [{ ...preview().changes[0]!, counts: { cleared: 211 } }] })
    await renderView(server(collection(blogSchema()), (call) =>
      call.url.endsWith('/preview') ? { status: 200, body: preview() }
        : call.method === 'PUT' ? problem(409, 'cms.preview_outdated', { preview: fresh })
          : undefined))

    await fireEvent.click(fieldButton('Reading time', 'Number'))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.field.remove }))
    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))
    await fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: en.cms.preview.confirm }))

    expect(await screen.findByText(en.cms.preview.outcome.cleared.replace('{count}', '211'))).toBeTruthy()
  })

  it('offers to reload after someone else saved first', async () => {
    await renderView(server(collection(blogSchema()), (call) => (call.method === 'PUT' ? problem(409, 'cms.revision_conflict', { currentRevision: 3 }) : undefined)))

    await fireEvent.click(fieldButton('Summary', 'Text'))
    await fireEvent.update(screen.getByLabelText(en.cms.field.name), 'Excerpt')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))

    expect(await screen.findByText(en.cms.builder.outOfDate)).toBeTruthy()
    expect(screen.getByRole('button', { name: en.cms.builder.reload })).toBeTruthy()
  })

  it('says when the collection is busy', async () => {
    await renderView(server(collection(blogSchema()), (call) => (call.method === 'PUT' ? problem(409, 'cms.collection_busy') : undefined)))

    await fireEvent.update(screen.getByLabelText(en.cms.create.name), 'Articles')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.builder.save }))

    expect(await screen.findByText(en.cms.errors.collection_busy)).toBeTruthy()
  })

  it('is read-only for an editor', async () => {
    const { container } = await renderView(server(collection(blogSchema(), { canManage: false })))

    expect(screen.getByText(en.cms.builder.readOnly)).toBeTruthy()
    expect(screen.getByLabelText(en.cms.create.name).matches(':disabled')).toBe(true)
    expect(screen.getByLabelText(en.cms.field.name).matches(':disabled')).toBe(true)
    expect(screen.queryByRole('button', { name: en.cms.builder.save })).toBeNull()
    expect(screen.queryByRole('button', { name: en.cms.add.add })).toBeNull()
    expect(await violations(container)).toEqual([])
  })

  it('explains a type change that may lose values', async () => {
    await renderView(server(collection(blogSchema())))

    await fireEvent.click(fieldButton('Summary', 'Text'))
    await fireEvent.update(screen.getByLabelText(en.cms.field.type), 'number')
    expect(screen.getByText(en.cms.field.typeRule.lossy)).toBeTruthy()

    await fireEvent.update(screen.getByLabelText(en.cms.field.type), 'image')
    expect(screen.getByText(en.cms.field.typeRule.forbidden)).toBeTruthy()
  })
})
