import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/request'

/**
 * Turns a refused publishing operation into a message for people (012 FR-040).
 *
 * The message comes from the translation lookup keyed by the server's `errorCode`, never from
 * its English `detail`. The counts a refusal carries (`componentIds`, `pageCount`) are passed
 * into the message. A code this screen does not know falls back to a generic message.
 */
export function publishingErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    const problem = (caught.problem ?? {}) as unknown as Record<string, unknown>

    if (code === 'identity.forbidden') {
      return t('publishing.errors.forbidden')
    }

    const [module, reason] = code.split('.', 2)
    const key = (module === 'pages' || module === 'editor') && reason ? `publishing.errors.${module}.${reason}` : ''
    if (key && te(key)) {
      const componentIds = Array.isArray(problem.componentIds) ? problem.componentIds : []
      return t(key, { count: componentIds.length || Number(problem.pageCount ?? 0) })
    }
  }

  return t('publishing.errors.unknown')
}

/** Whether a refusal means "someone changed it; reload and try again". */
export function isOutOfDate(caught: unknown): boolean {
  return caught instanceof ApiError && (caught.errorCode === 'pages.out_of_date' || caught.errorCode === 'editor.out_of_date')
}
