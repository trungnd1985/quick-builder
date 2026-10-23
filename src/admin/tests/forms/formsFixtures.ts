import { vi } from 'vitest'
import type { FormSummary, Submission, SubmissionPage, SubmissionSummary } from '../../src/api/forms'

export const SITE = '44444444-4444-4444-4444-444444444444'
export const SUB_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
export const SUB_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

export type Reply = { status: number; body?: unknown }
export type Call = { url: string; method: string; body?: unknown }

/** Answers every fetch with `reply(method, url, body)` and records the calls. */
export function stubFetch(reply: (method: string, url: string, body: unknown) => Reply): Call[] {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })
    const answer = reply(method, url, body)
    return {
      ok: answer.status < 300,
      status: answer.status,
      statusText: 'x',
      json: async () => answer.body,
      text: async () => JSON.stringify(answer.body ?? {}),
    }
  }))
  return calls
}

export const forms: FormSummary[] = [
  { formId: 'contact', name: 'Contact', published: true, retentionDays: 90, fields: [{ id: 'e1', label: 'Email', type: 'email' }], lastPublishedAt: '2026-10-01T10:00:00Z' },
  { formId: 'old', name: 'Old enquiry', published: false, retentionDays: null, fields: [], lastPublishedAt: '2026-09-01T10:00:00Z' },
]

export function summary(id: string, overrides: Partial<SubmissionSummary> = {}): SubmissionSummary {
  return {
    id,
    formId: 'contact',
    formName: 'Contact',
    formPublished: true,
    pagePath: '/contact',
    receivedAt: '2026-10-20T09:30:00Z',
    read: true,
    notification: 'sent',
    preview: 'anna@example.com · Hello',
    ...overrides,
  }
}

export function page(items: SubmissionSummary[], canDelete = true, next: string | null = null): SubmissionPage {
  return { items, next, permissions: { delete: canDelete } }
}

export function detail(overrides: Partial<Submission> = {}): Submission {
  return {
    ...summary(SUB_A),
    values: [
      { fieldId: 'e1', label: 'Email', type: 'email', value: 'anna@example.com' },
      { fieldId: 'm1', label: 'Message', type: 'textarea', value: 'Hello\nthere' },
      { fieldId: 't1', label: 'Topics', type: 'checkboxes', value: ['Sales', 'Support'] },
      { fieldId: 'p1', label: 'Phone', type: 'tel', value: '' },
    ],
    spam: false,
    retentionDays: 90,
    notificationError: null,
    ...overrides,
  }
}
