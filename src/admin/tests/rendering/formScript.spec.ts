import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, waitFor } from '@testing-library/vue'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// 018 US2 (tasks T041; FR-010, FR-011, FR-012; research FS-10): the published form script, the
// very file the server embeds and admits by hash. Its rules answer the shared value-rule fixture
// as the server's do, and on markup the renderer wrote (the form golden file) it checks before
// sending, shows the same summary and messages, sends with fetch asking for JSON, and shows the
// success panel or the server's answer.

const script = readFileSync(resolve(process.cwd(), '../Modules/Rendering/QuickBuilder.Rendering/Resources/form-script.js'), 'utf8')
const golden = readFileSync(resolve(process.cwd(), '../../tests/fixtures/rendering/golden/form-in-component.html'), 'utf8')

type Field = Record<string, unknown>
type Check = (field: Field, values: string[]) => { code: string | null; values: string[] }

interface ValueCase {
  name: string
  field: Field
  values: string[]
  expect: { code: string | null; values: string[] }
}

const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../tests/fixtures/forms/value-rules.json'), 'utf8')) as { cases: ValueCase[] }
const expand = (value: string) => value.replace(/\$\{(\d+)a\}/g, (_, n: string) => 'a'.repeat(Number(n)))

const run = () => new Function(script)()

/** The script's check, captured through its test hook, on a field shaped as the script reads it from the page. */
function scriptCheck(): Check {
  let captured: Check | null = null
  ;(window as unknown as { __qbFormRulesHook?: (c: Check) => void }).__qbFormRulesHook = (c) => (captured = c)
  run()
  delete (window as unknown as { __qbFormRulesHook?: unknown }).__qbFormRulesHook
  return captured!
}

/** A fixture field as the page carries it: the default maximum is written out, options are values. */
function asOnPage(field: Field): Field {
  const type = String(field.type)
  const defaultMax = type === 'textarea' ? 10000 : type === 'email' ? 254 : type === 'text' || type === 'phone' ? 500 : undefined
  return {
    ...field,
    type: type === 'consent' ? 'checkbox' : type,
    required: field.required === undefined ? type === 'consent' : field.required,
    maxLength: field.maxLength ?? defaultMax,
    whole: field.wholeNumber === true,
    options: Array.isArray(field.options) ? field.options.map((o) => String((o as { value: string }).value)) : [],
  }
}

describe('the form script\'s rules against the shared fixture', () => {
  const check = scriptCheck()

  it.each(fixture.cases.map((c) => [c.name, c] as const))('%s', (_name, testCase) => {
    expect(check(asOnPage(testCase.field), testCase.values.map(expand))).toEqual({
      code: testCase.expect.code,
      values: testCase.expect.values.map(expand),
    })
  })
})

describe('the form script on a published form', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    const parsed = new DOMParser().parseFromString(golden, 'text/html')
    document.body.innerHTML = parsed.body.innerHTML
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    run()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  const form = () => document.querySelector<HTMLFormElement>('form[data-qb-form]')!
  const input = (name: string) => form().querySelector<HTMLInputElement | HTMLTextAreaElement>(`[name="${name}"]`)!
  const submit = () => fireEvent.submit(form())

  it('takes over checking from the browser', () => {
    expect(form().noValidate).toBe(true)
  })

  it('checks before sending, and shows the summary, focused, and each field\'s message', async () => {
    await submit()

    const summary = form().querySelector<HTMLElement>('.qb-form__summary')!
    expect(summary).not.toBeNull()
    expect(document.activeElement).toBe(summary)
    expect(summary.querySelector('h2')!.textContent).toBe('There is a problem')
    expect([...summary.querySelectorAll('a')].map((a) => a.textContent)).toEqual(['Email is required.', 'Question is required.'])
    expect(summary.querySelector('a')!.getAttribute('href')).toBe(`#${input('f.c1d2e3f4a5b6').id}`)

    const email = input('f.c1d2e3f4a5b6')
    expect(email.getAttribute('aria-invalid')).toBe('true')
    const error = document.getElementById(email.getAttribute('aria-describedby')!.split(' ').pop()!)!
    expect(error.className).toBe('qb-form__error')
    expect(error.textContent).toBe('Email is required.')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('replaces old messages when the visitor tries again', async () => {
    await submit()
    input('f.c1d2e3f4a5b6').value = 'jane@'
    await submit()

    expect(form().querySelectorAll('.qb-form__summary')).toHaveLength(1)
    expect(form().querySelectorAll('.qb-form__error')).toHaveLength(2)
    expect(form().querySelector('.qb-form__summary a')!.textContent).toBe('Email must be an email address.')
    expect(input('f.c1d2e3f4a5b6').getAttribute('aria-describedby')!.split(' ')).toHaveLength(1)
  })

  it('sends valid values with fetch, asking for JSON, with the button disabled meanwhile, then shows the success panel', async () => {
    let answer: (value: unknown) => void = () => undefined
    fetchMock.mockReturnValue(new Promise((resolve) => (answer = resolve)))
    input('f.c1d2e3f4a5b6').value = 'jane@example.com'
    input('f.d1e2f3a4b5c6').value = 'Hello'

    await submit()
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toMatch(/\/_forms\/footer-form$/)
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>).Accept).toBe('application/json')
    expect((init.body as URLSearchParams).get('f.c1d2e3f4a5b6')).toBe('jane@example.com')
    expect((init.body as URLSearchParams).has('_page')).toBe(true)
    expect(form().querySelector<HTMLButtonElement>('.qb-form__submit')!.disabled).toBe(true)

    answer({ json: async () => ({ status: 'ok' }) })
    await waitFor(() => expect(form().closest('.qb-form')!.classList.contains('qb-form--sent')).toBe(true))
    expect(document.activeElement).toBe(document.querySelector('.qb-form__sent'))
  })

  it('shows the server\'s refusals as its own', async () => {
    fetchMock.mockResolvedValue({ json: async () => ({ status: 'invalid', errors: { d1e2f3a4b5c6: 'too_long' } }) })
    input('f.c1d2e3f4a5b6').value = 'jane@example.com'
    input('f.d1e2f3a4b5c6').value = 'Hello'

    await submit()

    await waitFor(() => expect(form().querySelector('.qb-form__summary a')?.textContent).toBe('Question must be at most 10000 characters.'))
    expect(form().querySelector<HTMLButtonElement>('.qb-form__submit')!.disabled).toBe(false)
  })

  it('says to try later when limited, and shows the error message when sending fails', async () => {
    input('f.c1d2e3f4a5b6').value = 'jane@example.com'
    input('f.d1e2f3a4b5c6').value = 'Hello'

    fetchMock.mockResolvedValueOnce({ json: async () => ({ status: 'limited', retryAfter: 30 }) })
    await submit()
    await waitFor(() => expect(form().querySelector('.qb-form__banner')?.textContent).toContain('Too many messages'))
    expect(document.activeElement).toBe(form().querySelector('.qb-form__banner'))

    fetchMock.mockRejectedValueOnce(new Error('offline'))
    await submit()
    await waitFor(() => expect(form().querySelector('.qb-form__banner')?.textContent).toBe(form().getAttribute('data-msg-error')))
    expect(form().querySelectorAll('.qb-form__banner')).toHaveLength(1)
  })
})
