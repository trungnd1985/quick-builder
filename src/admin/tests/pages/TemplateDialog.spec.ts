import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import TemplateDialog from '../../src/features/pages/TemplateDialog.vue'
import type { PageNode } from '../../src/api/pages'
import type { CollectionSummary } from '../../src/api/cms'
import { en, i18n, SITE, stubFetch, violations, type Reply } from '../cms/harness'

// 016 US1, US7 (tasks T029): the template dialog shows, before anything changes, what making a
// page a template, changing its collection or removing it would do, and names the refusal.

const POSTS = '0192f5a0-0000-7000-8000-00000000c001'
const PEOPLE = '0192f5a0-0000-7000-8000-00000000c002'

const page = (extra: Partial<PageNode> = {}): PageNode => ({
  id: 'p1', parentId: 'blog', position: 0, depth: 2, title: 'Post', slug: 'post', path: '/blog/post',
  isHome: false, childCount: 0, version: 3,
  publication: { state: 'published' } as PageNode['publication'],
  templateCollectionId: null,
  ...extra,
})

const collections: CollectionSummary[] = [
  { id: POSTS, name: 'Blog posts', slug: 'blog-posts', fieldCount: 3, itemCount: 40, revision: 1, updatedAtUtc: '2026-10-10T00:00:00Z' },
  { id: PEOPLE, name: 'People', slug: 'people', fieldCount: 1, itemCount: 3, revision: 1, updatedAtUtc: '2026-10-10T00:00:00Z' },
]

const impact = (extra: Record<string, unknown> = {}): Reply => ({
  status: 200,
  body: { pattern: '/blog/{slug}', refusal: null, liveStaticAddressLost: true, itemsLosingAddress: 0, itemsGainingAddress: 40, brokenBindings: 0, ...extra },
})

function renderDialog(mode: 'set' | 'remove', node = page()) {
  return render(TemplateDialog, {
    props: { dialogId: 'tpl', siteId: SITE, page: node, collections, mode, busy: false, error: null },
    global: { plugins: [i18n] },
  })
}

afterEach(() => vi.unstubAllGlobals())

describe('TemplateDialog', () => {
  it('shows the pattern, the live address lost and the items gained, then submits the chosen collection', async () => {
    const calls = stubFetch(() => impact())
    const { emitted, container } = renderDialog('set')

    expect(await screen.findByText(en.pages.template.pattern.replace('{pattern}', '/blog/{slug}'))).toBeTruthy()
    expect(screen.getByText(en.pages.template.liveAddressLost.replace('{address}', '/blog/post'))).toBeTruthy()
    expect(screen.getByText('40 published items will get an address once the template is published.')).toBeTruthy()
    expect(calls[0].url).toBe(`/api/v1/pages/sites/${SITE}/pages/p1/template?collectionId=${POSTS}`)
    expect(await violations(container)).toEqual([])

    await fireEvent.click(screen.getByRole('button', { name: en.pages.template.set }))
    // 017 FR-028: the live address it loses is offered a redirect, ticked by default.
    expect(emitted().submit).toEqual([[POSTS, true]])
  })

  it('assesses again when another collection is chosen, and counts the bindings that would break', async () => {
    const calls = stubFetch((call) => (call.url.includes(PEOPLE) ? impact({ itemsLosingAddress: 40, itemsGainingAddress: 3, brokenBindings: 2, liveStaticAddressLost: false }) : impact()))
    renderDialog('set', page({ templateCollectionId: POSTS, path: '/blog/{slug}' }))

    await fireEvent.update(screen.getByLabelText(en.pages.template.collection), PEOPLE)

    expect(await screen.findByText('40 published items will lose their address.')).toBeTruthy()
    expect(screen.getByText('2 bindings in the draft will no longer find their field.')).toBeTruthy()
    expect(calls.at(-1)!.url).toContain(`collectionId=${PEOPLE}`)
  })

  it('names the rule a change would break and does not offer to submit it', async () => {
    stubFetch(() => impact({ pattern: null, refusal: 'pages.template_needs_parent' }))
    renderDialog('set')

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', en.pages.errors.template_needs_parent)
    expect(screen.getByRole('button', { name: en.pages.template.set })).toHaveProperty('disabled', true)
  })

  it('removes a template after showing what stops working', async () => {
    const calls = stubFetch(() => impact({ pattern: null, itemsLosingAddress: 12, itemsGainingAddress: 0, liveStaticAddressLost: false, brokenBindings: 3 }))
    const { emitted } = renderDialog('remove', page({ templateCollectionId: POSTS, path: '/blog/{slug}' }))

    expect(await screen.findByText('12 published items will lose their address.')).toBeTruthy()
    expect(calls[0].url).toBe(`/api/v1/pages/sites/${SITE}/pages/p1/template`)

    await fireEvent.click(screen.getByRole('button', { name: en.pages.template.remove }))
    await waitFor(() => expect(emitted().submit).toEqual([[null, false]]))
  })

  it('closes with Escape', async () => {
    stubFetch(() => impact())
    const { emitted } = renderDialog('set')

    await fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(emitted().cancel).toHaveLength(1)
  })
})
