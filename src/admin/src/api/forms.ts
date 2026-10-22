// Typed client for forms and submissions (018 contracts/forms-api.yaml). Every site role reads,
// marks and exports submissions; deleting needs `forms.submissions.delete`, which the list
// reports in `permissions.delete` so the screen hides what the server would refuse.

import { API_ROOT, request } from './request'

/** A retention period in days; null keeps submissions until they are deleted. */
export type RetentionDays = 30 | 90 | 180 | 365

export const RETENTION_PERIODS: readonly RetentionDays[] = [30, 90, 180, 365]

/** Whether a submission's email went out (018 data-model §3). */
export type NotificationState = 'not_needed' | 'not_sent' | 'pending' | 'sent' | 'failed' | 'none'

export interface FormFieldSummary {
  id: string
  label: string
  type: string
}

/** A form the site has ever published (the registry). */
export interface FormSummary {
  formId: string
  name: string
  /** Live on a published page or component now. */
  published: boolean
  retentionDays: RetentionDays | null
  fields: FormFieldSummary[]
  lastPublishedAt: string
}

export interface SubmissionSummary {
  id: string
  formId: string
  formName: string
  formPublished: boolean
  pagePath: string
  receivedAt: string
  read: boolean
  notification: NotificationState
  /** The first values joined, at most 160 characters. */
  preview: string
}

export interface SubmissionValue {
  fieldId: string
  label: string
  type: string
  /** A list for a checkbox group, else one string. */
  value: string | string[]
}

export interface Submission extends SubmissionSummary {
  values: SubmissionValue[]
  spam: boolean
  retentionDays: RetentionDays | null
  notificationError: string | null
}

export interface SubmissionPage {
  items: SubmissionSummary[]
  /** The cursor of the next page; null on the last. */
  next: string | null
  permissions: { delete: boolean }
}

export type ReadFilter = 'all' | 'read' | 'unread'

/** The list and export filters. Dates are ISO date-times. */
export interface SubmissionFilters {
  box?: 'inbox' | 'spam'
  formId?: string
  from?: string
  to?: string
  read?: ReadFilter
  q?: string
}

export interface SpamCounter {
  formId: string
  trappedKept: number
  trappedDropped: number
  rateLimited: number
}

/** The longest search the server accepts. */
export const MAX_SEARCH_LENGTH = 200

/** The query string for `filters`, leaving out defaults and empty values. */
export function filterQuery(filters: SubmissionFilters, after?: string | null): string {
  const query = new URLSearchParams()
  if (filters.box && filters.box !== 'inbox') query.set('box', filters.box)
  if (filters.formId) query.set('formId', filters.formId)
  if (filters.from) query.set('from', filters.from)
  if (filters.to) query.set('to', filters.to)
  if (filters.read && filters.read !== 'all') query.set('read', filters.read)
  if (filters.q?.trim()) query.set('q', filters.q.trim())
  if (after) query.set('after', after)
  const text = query.toString()
  return text ? `?${text}` : ''
}

const site = (siteId: string) => `/forms/sites/${encodeURIComponent(siteId)}`

export const formsApi = {
  forms: (siteId: string): Promise<{ items: FormSummary[] }> => request(`${site(siteId)}/forms`),

  /** How many stored submissions `days` would delete now. */
  retentionImpact: (siteId: string, formId: string, days: RetentionDays): Promise<{ count: number }> =>
    request(`${site(siteId)}/forms/${encodeURIComponent(formId)}/retention-impact?days=${days}`),

  list: (siteId: string, filters: SubmissionFilters = {}, after?: string | null): Promise<SubmissionPage> =>
    request(`${site(siteId)}/submissions${filterQuery(filters, after)}`),

  /** One submission; opening it marks it read. */
  get: (siteId: string, submissionId: string): Promise<Submission> =>
    request(`${site(siteId)}/submissions/${encodeURIComponent(submissionId)}`),

  mark: (siteId: string, submissionId: string, read: boolean): Promise<void> =>
    request(`${site(siteId)}/submissions/${encodeURIComponent(submissionId)}/read`, { method: 'PUT', body: JSON.stringify({ read }) }),

  remove: (siteId: string, ids: string[]): Promise<{ deleted: number }> =>
    request(`${site(siteId)}/submissions`, { method: 'DELETE', body: JSON.stringify({ ids }) }),

  /** Moves a spam submission to the inbox, unread, and sends its notification. */
  restore: (siteId: string, submissionId: string): Promise<Submission> =>
    request(`${site(siteId)}/submissions/${encodeURIComponent(submissionId)}/restore`, { method: 'POST' }),

  unreadCount: (siteId: string): Promise<{ count: number }> => request(`${site(siteId)}/submissions/unread-count`),

  spamCounters: (siteId: string): Promise<{ items: SpamCounter[] }> => request(`${site(siteId)}/spam-counters`),

  emailStatus: (): Promise<{ configured: boolean }> => request('/forms/email-status'),

  /** Sends a test email to the signed-in Administrator. */
  testEmail: (): Promise<void> => request('/forms/email-test', { method: 'POST' }),

  /**
   * The CSV download address for the filtered inbox. A plain link, so the browser streams the
   * file to disk; the session cookie goes with it and a GET needs no request header.
   */
  exportUrl: (siteId: string, filters: SubmissionFilters = {}): string =>
    `${API_ROOT}${site(siteId)}/submissions/export${filterQuery({ ...filters, box: undefined })}`,
}
