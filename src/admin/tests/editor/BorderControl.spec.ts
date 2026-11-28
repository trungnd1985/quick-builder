import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import BorderControl from '../../src/editor/panel/controls/BorderControl.vue'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'
import type { Json } from '../../src/editor/schema/subsetValidator'

// 066 FR-008, FR-009, FR-012, research BB-08: one control for a block's whole border.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })
const b = en.editor.panel.border

beforeEach(() => setActivePinia(createPinia()))

function renderControl(value: Json | undefined, boxOnly = false) {
  return render(BorderControl, {
    props: { prop: 'border', id: 'prop-x-border', label: en.editor.panel.props.border, schema: {}, value, siteId: 's', boxOnly },
    global: { plugins: [i18n] },
  })
}

const select = (label: string) => screen.getByLabelText(label) as HTMLSelectElement
const options = (label: string) => Array.from(select(label).options).map((o) => o.textContent?.trim())

describe('the border control', () => {
  it('shows width, style, colour, corner radius and inner spacing, in that order', () => {
    const { container } = renderControl(undefined)

    const labels = Array.from(container.querySelectorAll('label')).map((l) => l.textContent?.trim())
    expect(labels).toEqual([b.width, b.style, b.color, b.radius, b.padding])
    expect(container.querySelectorAll('[data-prop]')).toHaveLength(1)
    expect(container.querySelector('[data-prop]')?.getAttribute('data-prop')).toBe('border')
  })

  it('has no inner spacing on a Section or Container, which have padding of their own', () => {
    renderControl(undefined, true)

    expect(screen.queryByLabelText(b.padding)).toBeNull()
    expect(screen.getByLabelText(b.radius)).toBeTruthy()
  })

  it('offers the fixed widths and styles by name', () => {
    renderControl(undefined)

    expect(options(b.width)).toEqual([b.widths.none, b.widths.thin, b.widths.medium, b.widths.thick])
    expect(options(b.style)).toEqual([b.styles.solid, b.styles.dashed, b.styles.dotted])
  })

  it('disables style and colour without a line, and keeps their values', () => {
    renderControl({ width: 'none', style: 'dashed', color: { token: 'color-brand' } })

    expect(select(b.style).disabled).toBe(true)
    expect(select(b.color).disabled).toBe(true)
    expect(select(b.style).value).toBe('dashed')
    expect(select(b.color).value).toBe('color-brand')
    expect(select(b.radius).disabled).toBe(false)
    expect(select(b.padding).disabled).toBe(false)
  })

  it('emits the whole border as one value for one change', async () => {
    const { emitted } = renderControl({ width: 'thin', radius: { token: 'radius-sm' } })

    await fireEvent.update(select(b.style), 'dotted')

    expect(emitted().change).toEqual([[{ width: 'thin', radius: { token: 'radius-sm' }, style: 'dotted' }]])
  })

  it('stores nothing when the border would change nothing', async () => {
    const { emitted } = renderControl({ width: 'thin' })

    await fireEvent.update(select(b.width), 'none')

    expect(emitted().change).toEqual([[undefined]])
  })

  it('keeps style and colour chosen under width none', async () => {
    const { emitted } = renderControl({ width: 'thin', style: 'dashed' })

    await fireEvent.update(select(b.width), 'none')

    expect(emitted().change).toEqual([[{ width: 'none', style: 'dashed' }]])
  })

  it('removes a token reference when its default is chosen', async () => {
    const { emitted } = renderControl({ width: 'thin', padding: { token: 'space-sm' } })

    await fireEvent.update(select(b.padding), '')

    expect(emitted().change).toEqual([[{ width: 'thin' }]])
  })

  it('labels the colour default with the Border colour, and the radius default as the block\'s own', () => {
    renderControl(undefined)

    expect(options(b.color)[0]).toBe(b.colorDefault.replace('{name}', 'Border'))
    expect(options(b.radius)[0]).toBe(b.radiusDefault)
    expect(options(b.padding)[0]).toBe(b.paddingNone)
  })

  it('lists the site\'s own corner radii by name, in list order (FR-009)', () => {
    const own: TokenSet = { ...defaultTokens, radii: [{ id: 'radius-card', name: 'Card', rem: 0.75 }, { id: 'radius-pill', name: 'Pill', rem: 100 }] }
    useEditorStore().tokens = own
    renderControl(undefined)

    expect(options(b.radius)).toEqual([b.radiusDefault, 'Card', 'Pill'])
  })

  it('lists the default radii for a set saved before them', () => {
    const old = { ...defaultTokens } as TokenSet
    delete old.radii
    useEditorStore().tokens = old
    renderControl(undefined)

    expect(options(b.radius)).toEqual([b.radiusDefault, 'None', 'Small', 'Medium', 'Large', 'Full'])
  })

  it('shows a radius naming a deleted token as the block default, and says so (FR-016)', () => {
    renderControl({ radius: { token: 'radius-gone' } })

    expect(select(b.radius).value).toBe('')
    expect(screen.getByText(b.deleted)).toBeTruthy()
  })

  it('removes the radius when "Block default" is chosen', async () => {
    const { emitted } = renderControl({ width: 'thin', radius: { token: 'radius-md' } })

    await fireEvent.update(select(b.radius), '')

    expect(emitted().change).toEqual([[{ width: 'thin' }]])
  })

  it('labels every control and passes the accessibility rules', async () => {
    const { container } = renderControl({ width: 'thin' })

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['label', 'select-name', 'aria-valid-attr-value', 'aria-allowed-attr'] } })
    expect(results.violations).toEqual([])
    for (const label of [b.width, b.style, b.color, b.radius, b.padding]) {
      const control = select(label)
      control.focus()
      expect(document.activeElement).toBe(control)
    }
  })
})
