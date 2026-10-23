import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { resetEmailStatus } from '../../src/editor/panel/controls/emailStatus'
import { documentWith, mountEditor, stubServer, type Server } from './harness'

// 018 US4 (FR-031 to FR-033, FR-037): who a form's notification goes to — at most ten valid
// addresses, a subject, values on or off, a reply field only among the form's email fields and
// only with values on — and a warning while the installation sends no email.

const STATUS = 'GET /api/v1/forms/email-status'
const fields = [
  { id: 'e00000000001', type: 'email', label: 'Your email' },
  { id: 'e00000000002', type: 'email', label: 'Other email' },
  { id: 'a00000000001', type: 'text', label: 'Name' },
]
const form = (notify?: object) => ({ id: 'form', type: 'form', props: { name: 'Contact', fields, ...(notify ? { notify } : {}) }, children: [] })
const page = (notify?: object) => documentWith({ id: 's1', type: 'section', props: {}, children: [form(notify)] })
const notifyOf = () => useEditorStore().root!.children[0].children[0].props.notify as Record<string, unknown> | undefined

async function openNotify(server: Server, configured = true): Promise<HTMLElement> {
  server.script(STATUS, { status: 200, body: { configured } })
  await mountEditor()
  useEditorStore().select('form')
  return waitFor(() => {
    const group = document.querySelector<HTMLElement>('[data-prop="notify"]')
    if (!group) throw new Error('no notify control yet')
    return group
  })
}

describe('the notification settings of a form', () => {
  beforeEach(() => resetEmailStatus())
  afterEach(() => vi.unstubAllGlobals())

  it('keeps up to ten valid recipients and refuses an invalid one by name', async () => {
    const control = await openNotify(stubServer(page()))
    const recipients = within(control).getByLabelText(en.editor.form.notify.recipients)

    await fireEvent.update(recipients, 'editor@example.com\nnot-an-address')
    await fireEvent.change(recipients)
    expect(within(control).getByText(en.editor.form.notify.recipientsInvalid.replace('{address}', 'not-an-address'))).toBeTruthy()
    expect(notifyOf()).toBeUndefined()

    const eleven = Array.from({ length: 11 }, (_, i) => `r${i}@example.com`).join('\n')
    await fireEvent.update(recipients, eleven)
    await fireEvent.change(recipients)
    expect(within(control).getByText(en.editor.form.notify.tooMany)).toBeTruthy()

    await fireEvent.update(recipients, 'editor@example.com, owner@example.com')
    await fireEvent.change(recipients)
    await waitFor(() => expect(notifyOf()).toEqual({ recipients: ['editor@example.com', 'owner@example.com'] }))
  })

  it('stores a subject and shows the default as a placeholder', async () => {
    const control = await openNotify(stubServer(page({ recipients: ['editor@example.com'] })))
    const subject = within(control).getByLabelText(en.editor.form.notify.subject) as HTMLInputElement

    expect(subject.placeholder).toBe('New submission: Contact')
    await fireEvent.update(subject, '  Website enquiry ')
    await fireEvent.change(subject)

    await waitFor(() => expect(notifyOf()).toEqual({ recipients: ['editor@example.com'], subject: 'Website enquiry' }))
  })

  it('offers only the form email fields for replies, and none while values are off', async () => {
    const control = await openNotify(stubServer(page({ recipients: ['editor@example.com'], replyFieldId: 'e00000000001' })))
    const reply = within(control).getByLabelText(en.editor.form.notify.replyField) as HTMLSelectElement

    expect(Array.from(reply.options).map((o) => o.textContent?.trim())).toEqual([en.editor.form.notify.noReplyField, 'Your email', 'Other email'])
    expect(reply.value).toBe('e00000000001')

    await fireEvent.click(within(control).getByLabelText(en.editor.form.notify.includeValues))

    await waitFor(() => expect(notifyOf()).toEqual({ recipients: ['editor@example.com'], includeValues: false }))
    expect(reply.disabled).toBe(true)
  })

  it('warns when the installation sends no email', async () => {
    const control = await openNotify(stubServer(page()), false)

    expect(await within(control).findByText(en.editor.form.notify.emailOff)).toBeTruthy()
  })

  it('shows no warning when email is on', async () => {
    const control = await openNotify(stubServer(page()), true)

    await waitFor(() => expect(within(control).getByLabelText(en.editor.form.notify.recipients)).toBeTruthy())
    expect(within(control).queryByText(en.editor.form.notify.emailOff)).toBeNull()
  })

  it('has no accessibility violations with the warning shown', async () => {
    const control = await openNotify(stubServer(page()), false)
    await within(control).findByText(en.editor.form.notify.emailOff)

    const results = await axe.run(control, { runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr'] } })
    expect(results.violations).toEqual([])
    expect(screen.getByRole('region', { name: en.editor.panel.title })).toBeTruthy()
  })
})
