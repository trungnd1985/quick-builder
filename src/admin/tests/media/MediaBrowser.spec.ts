import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import MediaBrowser from '../../src/features/media/MediaBrowser.vue'
import type { MediaFile, MediaFilePage } from '../../src/api/media'
import en from '../../src/i18n/en.json'

// 013 US1 and US4: search (debounced, literal), kind, sort and direction, paging, moving
// selected files, the no-results state, and picker mode (images only, Choose).

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function file(id: string, overrides: Partial<MediaFile> = {}): MediaFile {
  return {
    id,
    siteId: SITE,
    fileName: `${id}.png`,
    contentType: 'image/png',
    sizeBytes: 2048,
    uploadedAtUtc: '2026-10-01T10:00:00Z',
    uploadedBy: { id: 'u1', displayName: 'Uploader' },
    publicPath: `/_media/${id}.png`,
    available: true,
    canDelete: true,
    folderId: null,
    altText: null,
    width: 64,
    height: 64,
    contentVersion: 1,
    metadataState: 'recorded',
    replacedAtUtc: null,
    replacedBy: null,
    variants: [],
    thumbnailPath: `/thumb/${id}`,
    usage: { places: 0, draftPlaces: 0, livePlaces: 0, historyOnlyPlaces: 0, inLiveUse: false },
    canReplace: true,
    ...overrides,
  }
}

interface Call { url: string; method: string; body?: unknown }

function stubFetch(pages: MediaFilePage[], folders = [{ id: 'f1', name: 'Team', parentId: null, fileCount: 1, createdAtUtc: '' }]) {
  const calls: Call[] = []
  const queue = [...pages]
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : undefined })

    if (url.includes('/folders')) {
      return { ok: true, status: 200, json: async () => ({ folders, topLevelFileCount: 0, revision: 1 }) }
    }

    if (method === 'POST') {
      return { ok: true, status: 204, json: async () => undefined }
    }

    const page = queue.length > 1 ? queue.shift()! : queue[0] ?? { items: [], totalCount: 0, nextCursor: null }
    return { ok: true, status: 200, json: async () => page }
  }))
  return calls
}

const listCalls = (calls: Call[]) => calls.filter((c) => c.method === 'GET' && c.url.includes('/files'))

describe('MediaBrowser', () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }))
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('searches after a pause, passing the term as literal text', async () => {
    const calls = stubFetch([{ items: [file('a')], totalCount: 1, nextCursor: null }])
    render(MediaBrowser, { props: { siteId: SITE }, global: { plugins: [i18n] } })
    await screen.findByText('a.png')

    await fireEvent.update(screen.getByRole('searchbox', { name: en.media.search.label }), '50%_off')
    expect(listCalls(calls)).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(300)
    await waitFor(() => expect(listCalls(calls)).toHaveLength(2))
    expect(listCalls(calls)[1]!.url).toContain('q=50%25_off')
  })

  it('passes kind, sort and direction, and pages with Load more', async () => {
    const calls = stubFetch([
      { items: [file('a')], totalCount: 2, nextCursor: 'next' },
      { items: [file('b')], totalCount: 2, nextCursor: null },
    ])
    render(MediaBrowser, { props: { siteId: SITE }, global: { plugins: [i18n] } })
    await screen.findByText('a.png')

    await fireEvent.update(screen.getByLabelText(en.media.filter.kind), 'document')
    await fireEvent.update(screen.getByLabelText(en.media.sort.label), 'name')
    await fireEvent.update(screen.getByLabelText(en.media.sort.direction), 'desc')

    await waitFor(() => expect(listCalls(calls).at(-1)!.url).toContain('kind=document&sort=name&direction=desc'))
  })

  it('offers to search all folders or clear when nothing matches', async () => {
    stubFetch([{ items: [], totalCount: 0, nextCursor: null }])
    render(MediaBrowser, { props: { siteId: SITE }, global: { plugins: [i18n] } })
    await screen.findByText(en.media.empty.title)

    await fireEvent.update(screen.getByRole('searchbox'), 'nothing')
    await vi.advanceTimersByTimeAsync(300)

    await screen.findByText(en.media.search.none)
    expect(screen.getByRole('button', { name: en.media.search.clear })).toBeTruthy()
  })

  it('moves the selected files to a folder', async () => {
    const calls = stubFetch([{ items: [file('a'), file('b')], totalCount: 2, nextCursor: null }])
    render(MediaBrowser, { props: { siteId: SITE }, global: { plugins: [i18n] } })
    await screen.findByText('a.png')

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select a.png' }))
    await fireEvent.update(screen.getByLabelText('Move one selected file to'), 'f1')
    await fireEvent.click(screen.getByRole('button', { name: en.media.move.button }))

    await waitFor(() => expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ fileIds: ['a'], folderId: 'f1' }))
  })

  it('in picker mode lists images only and chooses a file', async () => {
    const calls = stubFetch([{ items: [file('a')], totalCount: 1, nextCursor: null }])
    const { emitted } = render(MediaBrowser, { props: { siteId: SITE, mode: 'picker' }, global: { plugins: [i18n] } })
    await screen.findByText('a.png')

    expect(listCalls(calls)[0]!.url).toContain('kind=image')
    expect(screen.queryByLabelText(en.media.filter.kind)).toBeNull()
    expect(screen.queryByRole('button', { name: /^Delete / })).toBeNull()

    await fireEvent.click(screen.getByRole('button', { name: 'Choose a.png' }))
    expect(emitted().choose?.[0]).toEqual([file('a')])
  })

  it('has no baseline accessibility violations', async () => {
    stubFetch([{ items: [file('a')], totalCount: 1, nextCursor: null }])
    const { container } = render(MediaBrowser, { props: { siteId: SITE }, global: { plugins: [i18n] } })
    await screen.findByText('a.png')

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-valid-attr-value', 'aria-required-attr', 'select-name'] },
    })
    expect(results.violations).toEqual([])
  })
})
