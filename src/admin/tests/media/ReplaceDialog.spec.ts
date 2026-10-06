import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import ReplaceDialog from '../../src/features/media/ReplaceDialog.vue'
import type { MediaFile, MediaUsageSummary } from '../../src/api/media'
import en from '../../src/i18n/en.json'

// 013 US6 (tasks T100): replacing a file — where it is used and that live pages change at once,
// the live-use rule before any upload, progress, the new version on success, and each refusal.

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const none: MediaUsageSummary = { places: 0, draftPlaces: 0, livePlaces: 0, historyOnlyPlaces: 0, inLiveUse: false }
const live: MediaUsageSummary = { places: 2, draftPlaces: 1, livePlaces: 1, historyOnlyPlaces: 0, inLiveUse: true }

function file(overrides: Partial<MediaFile> = {}): MediaFile {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    siteId: SITE,
    fileName: 'logo.png',
    contentType: 'image/png',
    sizeBytes: 10,
    uploadedAtUtc: '2026-10-01T10:00:00Z',
    uploadedBy: { id: 'u1', displayName: 'Uploader' },
    publicPath: '/_media/x.png',
    available: true,
    canDelete: true,
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
    usage: none,
    canReplace: true,
    ...overrides,
  }
}

/** A stand-in XMLHttpRequest answering the upload with a scripted response. */
class FakeXhr {
  static reply: { status: number; body: unknown } = { status: 200, body: {} }
  static urls: string[] = []
  status = 0
  statusText = ''
  responseText = ''
  withCredentials = false
  upload = { onprogress: null as ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null }
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  onabort: (() => void) | null = null
  private url = ''
  open(_method: string, url: string): void {
    this.url = url
  }
  setRequestHeader(): void {}
  getResponseHeader(): string | null {
    return null
  }
  send(): void {
    FakeXhr.urls.push(this.url)
    setTimeout(() => {
      this.upload.onprogress?.({ lengthComputable: true, loaded: 1, total: 2 })
      setTimeout(() => {
        this.status = FakeXhr.reply.status
        this.responseText = JSON.stringify(FakeXhr.reply.body)
        this.onload?.()
      }, 10)
    }, 0)
  }
}

function choose(container: Element): Promise<void> {
  const input = container.querySelector('input[type=file]') as HTMLInputElement
  Object.defineProperty(input, 'files', { value: [new File([new Uint8Array([1])], 'new.png', { type: 'image/png' })], configurable: true })
  return fireEvent.change(input)
}

describe('ReplaceDialog', () => {
  beforeEach(() => {
    FakeXhr.urls = []
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('says where it is used and that live pages change at once, accessibly', async () => {
    const { container } = render(ReplaceDialog, { props: { siteId: SITE, file: file({ usage: live }) }, global: { plugins: [i18n] } })

    expect(screen.getByText(/2 places use this file/)).toBeTruthy()
    expect(screen.getByText(new RegExp(en.media.replace.live))).toBeTruthy()
    expect((screen.getByLabelText(en.media.replace.choose) as HTMLInputElement).accept).toContain('.png')

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-dialog-name', 'aria-valid-attr-value'] } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('explains the live rule before any upload when the member may not replace it', () => {
    render(ReplaceDialog, { props: { siteId: SITE, file: file({ usage: live, canReplace: false }) }, global: { plugins: [i18n] } })

    expect(screen.getByText(en.media.errors.in_live_use)).toBeTruthy()
    expect(screen.queryByLabelText(en.media.replace.choose)).toBeNull()
  })

  it('shows progress and hands back the new version, sending the version it saw', async () => {
    const next = file({ contentVersion: 2, publicPath: '/_media/x-v2.png', replacedAtUtc: '2026-10-01T11:00:00Z', replacedBy: { id: 'u2', displayName: 'Dana' } })
    FakeXhr.reply = { status: 200, body: next }
    const { container, emitted } = render(ReplaceDialog, { props: { siteId: SITE, file: file() }, global: { plugins: [i18n] } })

    await choose(container)
    await screen.findByText('Uploading… 50%')
    await waitFor(() => expect(emitted().replaced?.[0]).toEqual([next]))
    expect(FakeXhr.urls[0]).toContain('/content?expectedContentVersion=1')
  })

  it.each([
    ['media.kind_mismatch', en.media.errors.kind_mismatch],
    ['media.conflict', en.media.errors.conflict],
    ['media.in_live_use', en.media.errors.in_live_use],
    ['media.dimensions_too_large', en.media.errors.dimensions_too_large],
    ['media.something_new', en.media.replace.failed],
  ])('says why %s was refused, and keeps the dialog open', async (code, message) => {
    FakeXhr.reply = { status: 400, body: { title: 'x', status: 400, errorCode: code } }
    const { container, emitted } = render(ReplaceDialog, { props: { siteId: SITE, file: file() }, global: { plugins: [i18n] } })

    await choose(container)

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain(message))
    expect(emitted().replaced).toBeUndefined()
  })

  it('closes with Escape', async () => {
    const { emitted } = render(ReplaceDialog, { props: { siteId: SITE, file: file() }, global: { plugins: [i18n] } })

    await fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })

    expect(emitted().cancel).toHaveLength(1)
  })
})
