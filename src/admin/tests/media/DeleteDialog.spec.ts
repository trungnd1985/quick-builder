import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import DeleteDialog from '../../src/features/media/DeleteDialog.vue'
import type { MediaFile, MediaUsageSummary } from '../../src/api/media'
import en from '../../src/i18n/en.json'

// 003 US3 and 013 US5 (tasks T095): a plain confirmation for an unused file; for a used one,
// the places it is used in and a second confirmation; for one visitors can see and the member
// may not delete, the reason and no way to proceed.

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const none: MediaUsageSummary = { places: 0, draftPlaces: 0, livePlaces: 0, historyOnlyPlaces: 0, inLiveUse: false }

function file(usage: MediaUsageSummary, canDelete = true): MediaFile {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    siteId: SITE,
    fileName: 'poster.png',
    contentType: 'image/png',
    sizeBytes: 10,
    uploadedAtUtc: '2026-10-01T10:00:00Z',
    uploadedBy: { id: 'u1', displayName: 'Uploader' },
    publicPath: '/_media/x.png',
    available: true,
    canDelete,
    folderId: null,
    altText: null,
    width: 1,
    height: 1,
    contentVersion: 1,
    metadataState: 'recorded',
    replacedAtUtc: null,
    replacedBy: null,
    variants: [],
    thumbnailPath: '/thumb',
    usage,
    canReplace: canDelete,
  }
}

async function renderDialog(target: MediaFile) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/sites/:siteId/pages/:pageId/edit', name: 'page-editor', component: { template: '<div />' } },
      { path: '/sites/:siteId/components/:componentId/edit', name: 'component-editor', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  return render(DeleteDialog, { props: { siteId: SITE, file: target, busy: false }, global: { plugins: [i18n, router] } })
}

const confirmButton = () => screen.getByRole('button', { name: en.media.delete.confirm }) as HTMLButtonElement

describe('DeleteDialog', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({
        summary: { places: 2, draftPlaces: 1, livePlaces: 1, historyOnlyPlaces: 0, inLiveUse: true },
        places: [
          { kind: 'page', id: '33333333-3333-3333-3333-333333333333', title: 'Home', inDraft: true, live: true, inHistory: false, livePagesThroughComponent: null },
          { kind: 'page', id: '44444444-4444-4444-4444-444444444444', title: 'Draft news', inDraft: true, live: false, inHistory: false, livePagesThroughComponent: null },
        ],
      }),
    })))
  })

  afterEach(() => vi.unstubAllGlobals())

  it('asks once for an unused file, with Cancel focused first', async () => {
    const { emitted } = await renderDialog(file(none))

    expect(screen.getByText(en.media.delete.body)).toBeTruthy()
    await waitFor(() => expect(document.activeElement?.textContent?.trim()).toBe(en.media.delete.cancel))
    expect(confirmButton().disabled).toBe(false)

    await fireEvent.click(confirmButton())
    expect(emitted().confirm?.[0]).toEqual([{ confirmInUse: false }])
    // No usage lookup for an unused file; the site read for the trash date (021 FR-002) is fine.
    expect(vi.mocked(fetch).mock.calls.filter(([url]) => String(url).includes('/usage'))).toHaveLength(0)
  })

  it('names the places using it and needs a second confirmation, accessibly', async () => {
    const used = { places: 2, draftPlaces: 1, livePlaces: 1, historyOnlyPlaces: 0, inLiveUse: true }
    const { emitted, container } = await renderDialog(file(used))

    expect(screen.getByText(en.media.delete.inUseBody)).toBeTruthy()
    await screen.findByRole('link', { name: 'Home' })
    expect(screen.getByRole('link', { name: 'Draft news' })).toBeTruthy()
    expect(confirmButton().disabled).toBe(true)

    await fireEvent.click(screen.getByRole('checkbox'))
    expect(confirmButton().disabled).toBe(false)
    await fireEvent.click(confirmButton())
    expect(emitted().confirm?.[0]).toEqual([{ confirmInUse: true }])

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'button-name', 'link-name', 'aria-valid-attr-value', 'aria-dialog-name'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('explains the live rule to someone who may not delete it, and offers no way on', async () => {
    const used = { places: 1, draftPlaces: 1, livePlaces: 1, historyOnlyPlaces: 0, inLiveUse: true }
    await renderDialog(file(used, false))

    expect(screen.getByText(en.media.errors.in_live_use)).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(confirmButton().disabled).toBe(true)
  })

  it('asks again when new usage arrives after a refusal', async () => {
    const used = { places: 1, draftPlaces: 1, livePlaces: 0, historyOnlyPlaces: 0, inLiveUse: false }
    const { rerender } = await renderDialog(file(used))

    await fireEvent.click(screen.getByRole('checkbox'))
    expect(confirmButton().disabled).toBe(false)

    await rerender({ siteId: SITE, file: file({ ...used, places: 2, draftPlaces: 2 }), busy: false })
    await waitFor(() => expect(confirmButton().disabled).toBe(true))
    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(false)
  })
})
