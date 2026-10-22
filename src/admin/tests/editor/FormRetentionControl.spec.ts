import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import FormRetentionControl from '../../src/editor/panel/controls/FormRetentionControl.vue'
import en from '../../src/i18n/en.json'

// 018 FR-023a and the spec's edge case on shortening retention: the choices, and how many
// submissions already received a new or shorter period would delete, said before publishing.

const SITE = '44444444-4444-4444-4444-444444444444'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

afterEach(() => vi.unstubAllGlobals())

function stubImpact(count: number) {
  const urls: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    urls.push(url)
    return { ok: true, status: 200, statusText: 'x', json: async () => ({ count }), text: async () => JSON.stringify({ count }) }
  }))
  return urls
}

function renderControl(value: number | undefined) {
  return render(FormRetentionControl, {
    props: { prop: 'retentionDays', id: 'retention', label: 'Keep submissions', schema: {}, value, siteId: SITE, formId: 'contact' },
    global: { plugins: [i18n] },
  })
}

const impactText = (count: number) => en.editor.form.retention.impact.replace('{count}', String(count))

describe('the retention control', () => {
  it('offers until deleted and the four periods', () => {
    stubImpact(0)
    renderControl(undefined)

    const options = Array.from((screen.getByLabelText('Keep submissions') as HTMLSelectElement).options).map((o) => o.textContent?.trim())
    expect(options).toEqual([en.editor.form.retention.off, '30 days', '90 days', '180 days', '365 days'])
  })

  it('says how many submissions turning retention on would delete', async () => {
    const urls = stubImpact(12)
    const { emitted } = renderControl(undefined)

    await fireEvent.update(screen.getByLabelText('Keep submissions'), '30')

    expect(emitted().change).toEqual([[30]])
    expect(await screen.findByText(impactText(12))).toBeTruthy()
    expect(urls).toEqual([`/api/v1/forms/sites/${SITE}/forms/contact/retention-impact?days=30`])
  })

  it('asks again when the period is shortened, but not when lengthened or turned off', async () => {
    const urls = stubImpact(3)
    const { emitted } = renderControl(180)

    await fireEvent.update(screen.getByLabelText('Keep submissions'), '365')
    await fireEvent.update(screen.getByLabelText('Keep submissions'), '')
    expect(urls).toEqual([])
    expect(emitted().change).toEqual([[365], [undefined]])

    await fireEvent.update(screen.getByLabelText('Keep submissions'), '90')
    expect(await screen.findByText(impactText(3))).toBeTruthy()
  })

  it('shows nothing when nothing would be deleted', async () => {
    const urls = stubImpact(0)
    renderControl(undefined)

    await fireEvent.update(screen.getByLabelText('Keep submissions'), '365')

    await waitFor(() => expect(urls).toHaveLength(1))
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('has no accessibility violations with the warning shown', async () => {
    stubImpact(5)
    const { container } = renderControl(undefined)
    await fireEvent.update(screen.getByLabelText('Keep submissions'), '30')
    await screen.findByRole('status')

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr'] } })
    expect(results.violations).toEqual([])
  })
})
