import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import EmptyTrashDialog from '../../../src/features/trash/EmptyTrashDialog.vue'
import en from '../../../src/i18n/en.json'

// 021 US5, FR-021: emptying asks for the number of entries, only entries trashed before the
// counts were read go, and a trash that changed meanwhile asks again.

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function serve(totals: { pages: number; components: number; items: number; files: number }, empty: { status: number; body: unknown }) {
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const text = String(url)
    if (init?.method === 'POST') {
      return { ok: empty.status < 300, status: empty.status, json: async () => empty.body, headers: new Headers() }
    }

    const total = ['pages', 'components', 'items', 'files'].map((k) => (text.includes(`/trash/${k}`) ? totals[k as keyof typeof totals] : null)).find((n) => n !== null) ?? 0
    return { ok: true, status: 200, json: async () => ({ entries: [], nextCursor: null, total }), headers: new Headers() }
  }))
}

const confirmButton = () => screen.getByRole('button', { name: en.trash.emptyTrash.confirm }) as HTMLButtonElement
const typeLabel = (total: number) => en.trash.emptyTrash.typeCount.replace('{total}', String(total))

describe('EmptyTrashDialog', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('stays disabled until the total is typed, then empties, accessibly', async () => {
    serve({ pages: 2, components: 0, items: 0, files: 0 }, { status: 200, body: { pages: 2, components: 0, failed: [] } })
    const { emitted, container } = render(EmptyTrashDialog, { props: { siteId: SITE }, global: { plugins: [i18n] } })

    const input = await screen.findByLabelText(typeLabel(2))
    expect(confirmButton().disabled).toBe(true)

    await fireEvent.update(input, '1')
    expect(confirmButton().disabled).toBe(true)
    await fireEvent.update(input, '2')
    expect(confirmButton().disabled).toBe(false)

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'button-name', 'aria-valid-attr-value', 'aria-dialog-name'] } })
    expect(results.violations.map((v) => v.id)).toEqual([])

    await fireEvent.click(confirmButton())
    await waitFor(() => expect(emitted().emptied?.[0]).toEqual([2]))
  })

  it('asks again when the trash changed since the counts were read', async () => {
    serve({ pages: 1, components: 0, items: 0, files: 0 }, { status: 409, body: { title: 'Conflict', status: 409, errorCode: 'trash.count_changed' } })
    const { emitted } = render(EmptyTrashDialog, { props: { siteId: SITE }, global: { plugins: [i18n] } })

    await fireEvent.update(await screen.findByLabelText(typeLabel(1)), '1')
    await fireEvent.click(confirmButton())

    expect(await screen.findByText(en.trash.emptyTrash.changed)).toBeTruthy()
    expect(emitted().emptied).toBeUndefined()
    expect(confirmButton().disabled).toBe(true)
  })
})
