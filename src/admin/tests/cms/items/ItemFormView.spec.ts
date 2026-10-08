import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ItemFormView from '../../../src/features/cms/items/ItemFormView.vue'
import { COLLECTION, en, i18n, ITEM, item, loaded, postCollection, postSchema, problem, renderAt, SITE, stubFetch, violations, type Call, type Reply } from './itemHarness'

// 015 US1: the form generated from the schema, the slug following the name, checks as the member
// types, the server's report, and what happens when the item or the schema changed meanwhile.

const NEW = `/sites/${SITE}/collections/${COLLECTION}/items/new`
const EDIT = `/sites/${SITE}/collections/${COLLECTION}/items/${ITEM}`

function server(extra: (call: Call) => Reply | undefined = () => undefined): (call: Call) => Reply {
  return (call) => {
    const answer = extra(call)
    if (answer) return answer
    if (call.url.includes('/slug-availability')) return { status: 200, body: { available: true, suggestion: 'x' } }
    if (call.url.endsWith(`/collections/${COLLECTION}`)) return { status: 200, body: postCollection() }
    if (call.url.endsWith(`/items/${ITEM}`) && call.method === 'GET') return { status: 200, body: item() }
    if (call.url.endsWith('/items/lookup')) return { status: 200, body: { items: [], missing: [] } }
    return { status: 200, body: {} }
  }
}

describe('ItemFormView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.spyOn(window, 'confirm').mockReturnValue(true)
  })
  afterEach(() => vi.restoreAllMocks())

  it('renders one input per field, in schema order, with help, required marks and no accessibility issues', async () => {
    const { container } = await renderAt(ItemFormView, NEW, server())
    await loaded(en.cms.items.form.loading)

    const fields = Array.from(container.querySelectorAll('[data-field-id]')).map((el) => el.getAttribute('data-field-id'))
    expect(fields).toEqual(postSchema().fields.map((f) => f.id))
    expect(screen.getByText('Shown as the heading.')).toBeTruthy()
    expect(screen.getByLabelText(/Title/)).toBeTruthy()
    expect(screen.getByRole('switch')).toBeTruthy()
    expect(screen.getByRole('combobox', { name: 'Category' })).toBeTruthy()
    expect(await violations(container)).toEqual([])
  })

  it('suggests the slug from the title until it is edited, and offers a free one when it is taken', async () => {
    await renderAt(ItemFormView, NEW, server((call) =>
      call.url.includes('slug-availability?slug=hello-world') ? { status: 200, body: { available: false, suggestion: 'hello-world-2' } } : undefined))
    await loaded(en.cms.items.form.loading)

    await fireEvent.update(screen.getByLabelText(/Title/), 'Hello World!')
    const slug = screen.getByLabelText(en.cms.items.form.slug) as HTMLInputElement
    expect(slug.value).toBe('hello-world')

    await waitFor(() => expect(screen.getByRole('button', { name: 'Use hello-world-2' })).toBeTruthy())
    await fireEvent.click(screen.getByRole('button', { name: 'Use hello-world-2' }))
    expect(slug.value).toBe('hello-world-2')

    await fireEvent.update(screen.getByLabelText(/Title/), 'Something else')
    expect(slug.value).toBe('hello-world-2')
  })

  it('shows a problem as the member types, links it from the summary, and marks the input', async () => {
    await renderAt(ItemFormView, NEW, server())
    await loaded(en.cms.items.form.loading)

    const title = screen.getByLabelText(/Title/)
    await fireEvent.update(title, 'A title that is far too long')

    expect(title.getAttribute('aria-invalid')).toBe('true')
    const summary = screen.getByRole('heading', { name: en.cms.items.form.problemsTitle }).parentElement!
    expect(within(summary).getByRole('link', { name: 'Title' }).getAttribute('href')).toBe('#item-field-f1')
    const error = document.getElementById('item-field-f1-error')!
    expect(error.textContent).toContain('20')
    expect(title.getAttribute('aria-describedby')).toContain('item-field-f1-error')
  })

  it('does not send a draft with a malformed value, and saves an incomplete one', async () => {
    const { calls, router } = await renderAt(ItemFormView, NEW, server((call) =>
      call.method === 'POST' && call.url.endsWith('/items')
        ? { status: 201, body: item({ id: 'created', name: null, slug: 'draft', draft: { schemaVersion: 1, values: {} }, report: { problems: [{ fieldId: 'f1', problem: 'missing_required' }] }, attention: true }) }
        : undefined))
    await loaded(en.cms.items.form.loading)

    await fireEvent.update(screen.getByLabelText(en.cms.items.form.slug), 'draft')
    await fireEvent.update(screen.getByLabelText(/Reading time/), 'seven')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.saveDraft }))
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
    expect(screen.getByText(en.cms.items.form.malformedIntro)).toBeTruthy()

    await fireEvent.update(screen.getByLabelText(/Reading time/), '')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.saveDraft }))
    await waitFor(() => expect(router.currentRoute.value.params.itemId).toBe('created'))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ slug: 'draft', values: { schemaVersion: 1, values: {} }, schemaRevision: 2 })
  })

  it('shows the problems only the server can see against their fields', async () => {
    await renderAt(ItemFormView, EDIT, server((call) =>
      call.method === 'PUT' ? problem(400, 'cms.invalid_item', { report: { problems: [{ fieldId: 'f7', problem: 'reference_missing' }] } }) : undefined))
    await loaded(en.cms.items.form.loading)

    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.saveDraft }))

    await waitFor(() => expect(document.getElementById('item-field-f7-error')?.textContent).toBe(en.cms.items.problems.reference_missing))
  })

  it('reloads the schema when it changed, keeps what was typed, and points at the changed field', async () => {
    let schemaVersion = 0
    const renamed = postSchema()
    renamed.fields[0] = { ...renamed.fields[0]!, name: 'Headline' }
    await renderAt(ItemFormView, EDIT, (call) => {
      if (call.method === 'PUT') return problem(409, 'cms.schema_changed', { schemaRevision: 3 })
      if (call.url.endsWith(`/collections/${COLLECTION}`)) return { status: 200, body: schemaVersion++ === 0 ? postCollection() : postCollection(renamed, 3) }
      return server()(call)
    })
    await loaded(en.cms.items.form.loading)

    await fireEvent.update(screen.getByLabelText(/Title/), 'Typed text')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.saveDraft }))

    await waitFor(() => expect(screen.getByText(en.cms.items.form.schemaChanged)).toBeTruthy())
    expect((screen.getByLabelText(/Headline/) as HTMLInputElement).value).toBe('Typed text')
    expect(screen.getByText(en.cms.items.form.fieldChanged)).toBeTruthy()
  })

  it('offers to reload when someone else changed the item, and keeps the input when fields are being updated', async () => {
    let refusal = 'cms.item_conflict'
    await renderAt(ItemFormView, EDIT, server((call) => (call.method === 'PUT' ? problem(409, refusal) : undefined)))
    await loaded(en.cms.items.form.loading)

    await fireEvent.update(screen.getByLabelText(/Title/), 'Mine')
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.saveDraft }))
    await waitFor(() => expect(screen.getByRole('button', { name: en.cms.items.form.reload })).toBeTruthy())

    refusal = 'cms.items_being_updated'
    await fireEvent.click(screen.getByRole('button', { name: en.cms.items.form.saveDraft }))
    await waitFor(() => expect(screen.getByText(en.cms.items.errors.items_being_updated)).toBeTruthy())
    expect((screen.getByLabelText(/Title/) as HTMLInputElement).value).toBe('Mine')
  })

  it('asks before leaving with unsaved changes', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    stubFetch(server())
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/sites/:siteId/collections/:collectionId/items', name: 'site-collection-items', component: { template: '<div>items</div>' } },
        { path: '/sites/:siteId/collections/:collectionId/items/:itemId', name: 'site-collection-item', component: ItemFormView },
      ],
    })
    await router.push(EDIT)
    await router.isReady()
    render({ template: '<RouterView />' }, { global: { plugins: [i18n, router] } })
    await loaded(en.cms.items.form.loading)

    await fireEvent.update(screen.getByLabelText(/Title/), 'Unsaved')
    await router.push(`/sites/${SITE}/collections/${COLLECTION}/items`).catch(() => undefined)

    expect(confirm).toHaveBeenCalledWith(en.cms.items.form.leaveConfirm)
    expect(router.currentRoute.value.params.itemId).toBe(ITEM)
  })
})
