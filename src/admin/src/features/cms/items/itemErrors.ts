import type { Composer } from 'vue-i18n'
import { ApiError, type Problem } from '../../../api/items'

/**
 * Messages for refused item operations and for the problems of an item's report (015 data-model
 * §2 and §6).
 *
 * Messages come from translations keyed by the server's `errorCode` or problem code, never from
 * its English `detail`. An unknown code falls back to a generic message, so a new server code
 * never shows raw text.
 */
export function itemErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    if (code === 'identity.forbidden') return t('cms.items.errors.forbidden')

    const key = code.startsWith('cms.') ? `cms.items.errors.${code.slice('cms.'.length)}` : ''
    if (key && te(key)) return t(key)
  }

  return t('cms.items.errors.unknown')
}

/** The message for one problem of a report, with its limit when it has one. */
export function problemMessage(t: Composer['t'], te: Composer['te'], problem: Problem): string {
  const key = `cms.items.problems.${problem.problem}`
  const format = problem.format ? t(`cms.items.problems.formats.${problem.format}`) : ''
  return te(key) ? t(key, { limit: problem.limit ?? '', format }) : t('cms.items.problems.malformed')
}

export const errorCodeOf = (caught: unknown): string | undefined => (caught instanceof ApiError ? caught.errorCode : undefined)
