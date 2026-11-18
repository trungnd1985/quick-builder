import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import PreviewButton from '../../../src/features/preview/PreviewButton.vue'
import { itemPreviewUrl, pagePreviewUrl } from '../../../src/features/preview/previewUrls'
import en from '../../../src/i18n/en.json'

// 022 FR-003, research PV-14: the preview opens only once what is on screen is saved.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
const URL = '/_preview/sites/s/pages/p'

describe('PreviewButton', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('saves first, then opens the preview in a new tab', async () => {
    const open = vi.fn()
    vi.stubGlobal('open', open)
    const order: string[] = []
    const beforeOpen = vi.fn(async () => {
      order.push('save')
      return true
    })
    open.mockImplementation(() => order.push('open'))

    render(PreviewButton, { props: { url: URL, beforeOpen }, global: { plugins: [i18n] } })
    await fireEvent.click(screen.getByRole('button', { name: en.preview.open }))

    await waitFor(() => expect(open).toHaveBeenCalledWith(URL, '_blank', 'noopener'))
    expect(order).toEqual(['save', 'open'])
  })

  it('opens nothing when the save is refused', async () => {
    const open = vi.fn()
    vi.stubGlobal('open', open)
    const beforeOpen = vi.fn(async () => false)

    render(PreviewButton, { props: { url: URL, beforeOpen }, global: { plugins: [i18n] } })
    await fireEvent.click(screen.getByRole('button', { name: en.preview.open }))

    await waitFor(() => expect(beforeOpen).toHaveBeenCalled())
    expect(open).not.toHaveBeenCalled()
  })

  it('is disabled with its reason', () => {
    render(PreviewButton, { props: { url: URL, disabled: true, disabledReason: 'No template' }, global: { plugins: [i18n] } })

    const button = screen.getByRole('button', { name: en.preview.open }) as HTMLButtonElement
    expect(button.disabled).toBe(true)
    expect(button.title).toBe('No template')
  })

  it('builds member preview addresses on the admin origin', () => {
    expect(pagePreviewUrl('s', 'p')).toBe('/_preview/sites/s/pages/p')
    expect(pagePreviewUrl('s', 'p', { version: 3 })).toBe('/_preview/sites/s/pages/p?version=3')
    expect(pagePreviewUrl('s', 'p', { item: 'i' })).toBe('/_preview/sites/s/pages/p?item=i')
    expect(itemPreviewUrl('s', 'i')).toBe('/_preview/sites/s/items/i')
    expect(itemPreviewUrl('s', 'i', 2)).toBe('/_preview/sites/s/items/i?version=2')
  })
})
