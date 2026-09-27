import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/pages'

/**
 * Turns a refused page or component operation into a message for people (010 FR-034).
 *
 * The message comes from the translation lookup keyed by the server's `errorCode`, never
 * from its English `detail`. Counts the server sends with a refusal (`childCount`,
 * `usageCount`) are passed into the message. A code this screen does not know falls back
 * to a generic message.
 */
export function pageErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    const problem = (caught.problem ?? {}) as unknown as Record<string, unknown>

    if (code === 'request.invalid') {
      return t('pages.errors.invalid')
    }

    if (code === 'identity.forbidden') {
      return t('pages.errors.forbidden')
    }

    const key = code.startsWith('pages.') ? `pages.errors.${code.slice('pages.'.length)}` : ''
    if (key && te(key)) {
      return t(key, {
        count: Number(problem.childCount ?? problem.usageCount ?? 0),
        detail: caught.problem?.detail ?? '',
      })
    }
  }

  return t('pages.errors.unknown')
}

/** Whether a refusal means "reload and try again" rather than "fix your input". */
export function isOutOfDate(caught: unknown): boolean {
  return caught instanceof ApiError && caught.errorCode === 'pages.out_of_date'
}
