import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import MediaLibraryView from '../src/features/media/MediaLibraryView.vue'
import type { MediaFile, MediaFilePage } from '../src/api/media'
import en from '../src/i18n/en.json'

// 003 US1 and US3, FR-027, FR-028, SC-013: the media screen is usable by keyboard, every
// result is announced, every string comes from the lookup, and the buttons offered match
// what the server will allow.

const SITE = '11111111-1111-1111-1111-111111111111'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function file(id: string, overrides: Partial<MediaFile> = {}): MediaFile {
  return {
    id,
    siteId: SITE,
    fileName: `file-${id}.png`,
    contentType: 'image/png',
    sizeBytes: 2048,
    uploadedAtUtc: '2026-09-25T10:00:00Z',
    uploadedBy: { id: 'u1', displayName: 'Uploader' },
    publicPath: `/_media/${id}.png`,
    available: true,
    canDelete: true,
    ...overrides,
  }
}

interface Routes {
  sites?: unknown
  pages?: MediaFilePage[]
  deleteStatus?: number
}

/** Answers the admin API by URL, as the server would. */
function stubFetch(routes: Routes) {
  const pages = [...(routes.pages ?? [{ items: [], totalCount: 0, nextCursor: null }])]
  const calls: { url: string; method: string }[] = []

  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    calls.push({ url, method })

    if (url === '/api/v1/sites') {
      return { ok: true, status: 200, json: async () => routes.sites ?? [{ id: SITE, name: 'Site A', hostNames: ['site-a.test'], createdAtUtc: '2026-09-25T00:00:00Z' }] }
    }

    if (method === 'DELETE') {
      const status = routes.deleteStatus ?? 204
      return {
        ok: status < 300,
        status,
        statusText: 'x',
        text: async () => JSON.stringify({ title: 'Forbidden', status, errorCode: 'identity.forbidden' }),
        json: async () => ({}),
      }
    }

    const page = pages.shift() ?? { items: [], totalCount: 0, nextCursor: null }
    return { ok: true, status: 200, json: async () => page }
  })

  vi.stubGlobal('fetch', fetchMock)
  return calls
}

/** A stand-in XMLHttpRequest that answers each upload with a scripted response. */
class FakeXhr {
  static responses: { status: number; body: unknown }[] = []
  static sent: { headers: Record<string, string> }[] = []

  status = 0
  statusText = ''
  responseText = ''
  withCredentials = false
  upload: { onprogress: ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null } = { onprogress: null }
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  onabort: (() => void) | null = null
  private headers: Record<string, string> = {}

  open(): void {}

  setRequestHeader(name: string, value: string): void {
    this.headers[name] = value
  }

  getResponseHeader(): string | null {
    return null
  }

  send(): void {
    FakeXhr.sent.push({ headers: this.headers })
    const next = FakeXhr.responses.shift() ?? { status: 201, body: file('uploaded') }
    setTimeout(() => {
      this.upload.onprogress?.({ lengthComputable: true, loaded: 1, total: 2 })
      this.status = next.status
      this.responseText = JSON.stringify(next.body)
      this.onload?.()
    }, 0)
  }
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites', name: 'sites', component: { template: '<div />' } },
      { path: '/sites/:siteId/media', name: 'site-media', component: MediaLibraryView },
    ],
  })
  await router.push(`/sites/${SITE}/media`)
  await router.isReady()

  return render(MediaLibraryView, { global: { plugins: [i18n, router] } })
}

async function baselineViolations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: {
      type: 'rule',
      values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'aria-dialog-name'],
    },
  })
  return results.violations
}

function chooseFiles(container: Element, files: File[]): Promise<void> {
  const input = container.querySelector('input[type=file]') as HTMLInputElement
  Object.defineProperty(input, 'files', { value: files, configurable: true })
  return fireEvent.change(input)
}

describe('MediaLibraryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    FakeXhr.responses = []
    FakeXhr.sent = []
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the privacy notice before anything is uploaded, with a keyboard-operable picker (FR-027)', async () => {
    stubFetch({})
    const { container } = await renderView()

    await waitFor(() => expect(screen.getByText(en.media.empty.title)).toBeTruthy())

    expect(screen.getByText(en.media.upload.notice)).toBeTruthy()
    const choose = screen.getByRole('button', { name: en.media.upload.choose })
    expect(choose.tagName).toBe('BUTTON')
    // The native file input is not a second tab stop; the real button stands in for it.
    expect(container.querySelector('input[type=file]')?.getAttribute('tabindex')).toBe('-1')

    expect(await baselineViolations(container)).toEqual([])
  })

  it('shows loading, then an error distinct from empty when the list fails', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => url === '/api/v1/sites'
      ? { ok: true, status: 200, json: async () => [] }
      : { ok: false, status: 500, statusText: 'Boom', text: async () => '{"title":"Boom","status":500,"errorCode":"internal.unexpected"}' }))

    await renderView()

    await waitFor(() => expect(screen.getByText(en.media.error.title)).toBeTruthy())
    expect(screen.queryByText(en.media.empty.title)).toBeNull()
  })

  it('announces each upload result, and a refusal in words, with the right headers (US1, FR-028)', async () => {
    stubFetch({})
    FakeXhr.responses = [
      { status: 201, body: file('new-1', { fileName: 'good.png' }) },
      { status: 415, body: { title: 'Unsupported type', status: 415, errorCode: 'media.unsupported_type' } },
    ]
    const { container } = await renderView()
    await waitFor(() => expect(screen.getByText(en.media.empty.title)).toBeTruthy())

    await chooseFiles(container, [
      new File(['x'], 'good.png', { type: 'image/png' }),
      new File(['<script>'], 'bad.png', { type: '' }),
    ])

    const results = screen.getByRole('list', { name: en.media.upload.resultsLabel })
    expect(results.getAttribute('aria-live')).toBe('polite')

    await waitFor(() => expect(within(results).getByText('good.png: uploaded')).toBeTruthy())
    await waitFor(() => expect(within(results).getByText(`bad.png: ${en.media.errors.unsupported_type}`)).toBeTruthy())

    // The accepted file joins the list without a reload.
    await waitFor(() => expect(screen.getByText('good.png', { selector: 'td' })).toBeTruthy())

    expect(FakeXhr.sent[0]?.headers['X-QuickBuilder-Request']).toBe('1')
    expect(FakeXhr.sent[0]?.headers['X-QuickBuilder-File-Name']).toBe('good.png')
    expect(FakeXhr.sent[1]?.headers['Content-Type']).toBe('application/octet-stream')
  })

  it('falls back to a generic message for an error it does not know', async () => {
    stubFetch({})
    FakeXhr.responses = [{ status: 500, body: { title: 'x', status: 500, errorCode: 'internal.unexpected' } }]
    const { container } = await renderView()
    await waitFor(() => expect(screen.getByText(en.media.empty.title)).toBeTruthy())

    await chooseFiles(container, [new File(['x'], 'a.png', { type: 'image/png' })])

    await waitFor(() => expect(screen.getByText(`a.png: ${en.media.errors.unknown}`)).toBeTruthy())
  })

  it('loads more and moves focus to the first new file (US3)', async () => {
    stubFetch({
      pages: [
        { items: [file('a'), file('b')], totalCount: 3, nextCursor: 'next' },
        { items: [file('c')], totalCount: 3, nextCursor: null },
      ],
    })
    const { container } = await renderView()

    const more = await screen.findByRole('button', { name: en.media.loadMore })
    more.focus()
    await fireEvent.click(more)

    await waitFor(() => expect(document.activeElement?.getAttribute('data-file-id')).toBe('c'))
    expect(container.querySelectorAll('tbody tr')).toHaveLength(3)
    expect(screen.queryByRole('button', { name: en.media.loadMore })).toBeNull()
  })

  it('offers Delete only where the server allows it, and confirms in a trapped dialog (US3, Q4)', async () => {
    const calls = stubFetch({
      pages: [{ items: [file('mine', { canDelete: true }), file('theirs', { canDelete: false })], totalCount: 2, nextCursor: null }],
    })
    const { container } = await renderView()
    await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(2))

    expect(screen.getAllByRole('button', { name: /^Delete / })).toHaveLength(1)
    const trigger = screen.getByRole('button', { name: 'Delete file-mine.png' })

    await fireEvent.click(trigger)
    const dialog = await screen.findByRole('alertdialog')
    const cancel = within(dialog).getByRole('button', { name: en.media.delete.cancel })
    const confirm = within(dialog).getByRole('button', { name: en.media.delete.confirm })

    // The safe choice has focus, and Tab stays inside.
    await waitFor(() => expect(document.activeElement).toBe(cancel))
    await fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(document.activeElement).toBe(confirm)
    await fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(document.activeElement).toBe(cancel)

    // Escape cancels and returns focus to the row's button; nothing was deleted.
    await fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(trigger))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)

    // Confirming deletes and removes the row.
    await fireEvent.click(trigger)
    await fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: en.media.delete.confirm }))

    await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(1))
    expect(calls.find((c) => c.method === 'DELETE')?.url).toBe(`/api/v1/media/sites/${SITE}/files/mine`)
    expect(await baselineViolations(container)).toEqual([])
  })

  it('copies the public address built from the admin scheme and the first host name (US3)', async () => {
    stubFetch({ pages: [{ items: [file('a')], totalCount: 1, nextCursor: null }] })
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })

    await renderView()
    await fireEvent.click(await screen.findByRole('button', { name: 'Copy address of file-a.png' }))

    expect(writeText).toHaveBeenCalledWith(`${window.location.protocol}//site-a.test/_media/a.png`)
    await waitFor(() => expect(screen.getByText(en.media.copy.done)).toBeTruthy())
  })

  it('explains a site without host names and offers no copy action', async () => {
    stubFetch({
      sites: [{ id: SITE, name: 'Site A', hostNames: [], createdAtUtc: '2026-09-25T00:00:00Z' }],
      pages: [{ items: [file('a')], totalCount: 1, nextCursor: null }],
    })

    await renderView()

    await waitFor(() => expect(screen.getByText(en.media.noHost)).toBeTruthy())
    expect(screen.queryByRole('button', { name: /^Copy address/ })).toBeNull()
  })

  it('marks a file whose bytes are missing as unavailable', async () => {
    stubFetch({ pages: [{ items: [file('gone', { available: false })], totalCount: 1, nextCursor: null }] })

    await renderView()

    await waitFor(() => expect(screen.getByText(en.media.table.unavailable)).toBeTruthy())
  })

  it('renders every visible string through the lookup (SC-013)', async () => {
    stubFetch({ pages: [{ items: [file('a')], totalCount: 1, nextCursor: null }] })
    const { container } = await renderView()
    await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(1))

    // A string that is not in en.json is either data (names, dates, sizes, types) or a leak.
    const allowedData = ['file-a.png', 'image/png', 'Uploader', 'Site A', 'Media — Site A']
    const values = JSON.stringify(en)
    const texts = Array.from(container.querySelectorAll('h1, h2, p, button, th, a, span'))
      .map((el) => el.textContent?.trim() ?? '')
      .filter((text) => text.length > 0 && !/\d/.test(text))

    for (const text of texts) {
      expect(values.includes(text) || allowedData.includes(text), `"${text}" is not in the lookup`).toBe(true)
    }
  })
})
