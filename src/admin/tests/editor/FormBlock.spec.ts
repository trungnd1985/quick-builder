import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { withNewIds } from '../../src/editor/ops/tree'
import type { ContentNode } from '../../src/editor/schema/schema'
import { documentWith, mountEditor, press, stubServer } from './harness'

// 018 US1 (tasks T025): a Form block arrives from the palette ready to publish, its fields are
// added, edited, moved and removed from the panel by mouse and keyboard, what publishing would
// refuse is listed with a way to the field, and a copied form is a new form.

const field = (id: string, label: string, extra: object = {}) => ({ id, type: 'text', label, ...extra })
const form = (fields: object[], props: object = {}) => ({ id: 'form', type: 'form', props: { name: 'Contact', fields, ...props }, children: [] })
const page = (...blocks: object[]) => documentWith({ id: 's1', type: 'section', props: {}, children: blocks })
const option = (container: Element, type: string) => container.querySelector<HTMLElement>(`[role="option"][data-block-type="${type}"]`)!
const panel = () => screen.getByRole('region', { name: en.editor.panel.title })
const fieldsOf = () => useEditorStore().root!.children[0].children[0].props.fields as { id: string; label: string; type: string }[]

async function selectForm(): Promise<HTMLElement> {
  useEditorStore().select('form')
  return waitFor(() => {
    const list = panel().querySelector<HTMLElement>('[data-prop="fields"]')
    if (!list) throw new Error('no field list yet')
    return list
  })
}

describe('the Form block in the editor', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('arrives from the palette named, with one required email field', async () => {
    stubServer(page())
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('s1')

    const item = option(container, 'form')
    item.focus()
    await fireEvent.keyDown(item, { key: 'Enter' })

    const added = store.root!.children[0].children[0]
    expect(added.type).toBe('form')
    expect(added.props.name).toBe(en.editor.form.defaultName)
    const fields = added.props.fields as { id: string; type: string; required: boolean }[]
    expect(fields).toHaveLength(1)
    expect(fields[0]).toMatchObject({ type: 'email', required: true })
    expect(fields[0].id).toMatch(/^[0-9a-f]{12}$/)
  })

  it('draws the published markup, disabled, with the default texts', async () => {
    stubServer(page(form([field('a00000000001', 'Name', { required: true })])))
    const { container } = await mountEditor()

    const drawn = await waitFor(() => container.querySelector<HTMLElement>('.editor-canvas-scroll .qb-form')!)
    expect(drawn.querySelector('.qb-form__req')?.textContent).toBe(' (required)')
    expect(drawn.querySelector<HTMLButtonElement>('.qb-form__submit')!.disabled).toBe(true)
    expect(drawn.querySelector<HTMLInputElement>('.qb-form__input')!.disabled).toBe(true)
    expect(drawn.querySelector('.qb-form__sent p')?.textContent).toBe(en.editor.form.public.success)
  })

  it('adds a field through the dialog, with a new id', async () => {
    stubServer(page(form([field('a00000000001', 'Name')])))
    await mountEditor()
    const list = await selectForm()

    await fireEvent.update(within(list).getByRole('combobox', { name: en.editor.form.fields.addType }), 'textarea')
    await fireEvent.click(within(list).getByRole('button', { name: en.editor.form.fields.add }))
    const dialog = await screen.findByRole('dialog', { name: en.editor.form.field.newTitle })
    await fireEvent.update(within(dialog).getByLabelText(en.editor.form.field.label), 'Message')
    await fireEvent.update(within(dialog).getByLabelText(en.editor.form.field.maxLength), '2000')
    await fireEvent.click(within(dialog).getByRole('checkbox', { name: en.editor.form.field.required }))
    await fireEvent.click(within(dialog).getByRole('button', { name: en.editor.form.field.save }))

    await waitFor(() => expect(fieldsOf()).toHaveLength(2))
    const added = fieldsOf()[1]
    expect(added).toMatchObject({ type: 'textarea', label: 'Message', maxLength: 2000, required: true })
    expect(added.id).toMatch(/^[0-9a-f]{12}$/)
    expect(added.id).not.toBe('a00000000001')
  })

  it('refuses a field without a label, and edits keep the field id', async () => {
    stubServer(page(form([field('a00000000001', 'Name')])))
    await mountEditor()
    const list = await selectForm()

    await fireEvent.click(within(list).getByRole('button', { name: en.editor.form.fields.edit.replace('{label}', 'Name') }))
    const dialog = await screen.findByRole('dialog', { name: en.editor.form.field.editTitle })
    await fireEvent.update(within(dialog).getByLabelText(en.editor.form.field.label), ' ')
    await fireEvent.click(within(dialog).getByRole('button', { name: en.editor.form.field.save }))
    expect(within(dialog).getByRole('alert').textContent).toContain(en.editor.form.field.labelRequired)

    await fireEvent.update(within(dialog).getByLabelText(en.editor.form.field.label), 'Full name')
    await fireEvent.click(within(dialog).getByRole('button', { name: en.editor.form.field.save }))
    await waitFor(() => expect(fieldsOf()[0]).toEqual({ id: 'a00000000001', type: 'text', label: 'Full name' }))
  })

  it('moves fields with the buttons and with Alt+arrows, and says where they went', async () => {
    stubServer(page(form([field('a00000000001', 'A'), field('a00000000002', 'B'), field('a00000000003', 'C')])))
    await mountEditor()
    const list = await selectForm()

    await fireEvent.click(within(list).getByRole('button', { name: en.editor.form.fields.moveDown.replace('{label}', 'A') }))
    await waitFor(() => expect(fieldsOf().map((f) => f.label)).toEqual(['B', 'A', 'C']))
    expect(list.querySelector('[aria-live="polite"]')!.textContent).toContain('A moved to position 2 of 3')

    const row = list.querySelector<HTMLElement>('[data-field-id="a00000000003"]')!
    press(row, 'ArrowUp', { alt: true })
    await waitFor(() => expect(fieldsOf().map((f) => f.label)).toEqual(['B', 'C', 'A']))
  })

  it('removes a field only after confirming', async () => {
    stubServer(page(form([field('a00000000001', 'A'), field('a00000000002', 'B')])))
    await mountEditor()
    const list = await selectForm()
    const confirm = vi.fn(() => false)
    vi.stubGlobal('confirm', confirm)

    const remove = within(list).getByRole('button', { name: en.editor.form.fields.remove.replace('{label}', 'A') })
    await fireEvent.click(remove)
    expect(fieldsOf()).toHaveLength(2)

    confirm.mockReturnValue(true)
    await fireEvent.click(remove)
    await waitFor(() => expect(fieldsOf().map((f) => f.label)).toEqual(['B']))
  })

  it('lists what publishing would refuse, each with a way to its field', async () => {
    stubServer(page(form([field('a00000000001', 'Name'), field('a00000000002', 'name'), { id: 'a00000000003', type: 'select', label: 'Topic' }])))
    const { container } = await mountEditor()
    const list = await selectForm()

    const problems = panel().querySelector<HTMLElement>('.form-problems')!
    expect(problems.textContent).toContain(en.editor.form.problems.duplicate_label.replace('{label}', 'name'))
    expect(problems.textContent).toContain(en.editor.form.problems.no_options.replace('{label}', 'Topic'))
    expect(container.querySelector('.qb-editor-form-note')?.textContent).toContain(en.editor.form.incomplete)

    await fireEvent.click(within(problems).getByRole('button', { name: en.editor.form.problems.goTo.replace('{label}', 'Topic') }))
    expect(document.activeElement).toBe(list.querySelector('[data-field-id="a00000000003"] [data-part="edit"]'))
  })

  it('makes a copied form a new form whose fields keep their ids', () => {
    const original = form([field('a00000000001', 'Name')]) as unknown as ContentNode

    const copy = withNewIds(original)

    expect(copy.id).not.toBe(original.id)
    expect(copy.props.fields).toEqual(original.props.fields)
  })

  it('shows the English texts visitors read, as the published page does', () => {
    const publicTexts = JSON.parse(readFileSync(
      resolve(process.cwd(), '../Modules/Rendering/QuickBuilder.Rendering/Resources/public-texts/en.json'), 'utf8')) as {
      forms: { submit: string; success: string; error: string; required: string; trap: string }
    }

    expect(en.editor.form.public).toEqual({
      submit: publicTexts.forms.submit,
      success: publicTexts.forms.success,
      error: publicTexts.forms.error,
      required: publicTexts.forms.required,
      trap: publicTexts.forms.trap,
    })
  })

  it('has a panel with no accessibility violations', async () => {
    stubServer(page(form([field('a00000000001', 'Name'), { id: 'a00000000002', type: 'checkboxes', label: 'Pick', options: [{ value: 'a', label: 'A' }] }])))
    await mountEditor()
    await selectForm()

    const results = await axe.run(panel(), { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })
})
