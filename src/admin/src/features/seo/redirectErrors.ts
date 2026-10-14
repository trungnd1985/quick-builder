import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/request'

/**
 * Turns a refused redirect operation into a message for people (017 data-model §4).
 *
 * The message comes from the translation lookup keyed by the server's `errorCode`, never from
 * its English `detail`, which may name the source address. A code this screen does not know
 * falls back to a generic message.
 */
export function redirectErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    const problem = (caught.problem ?? {}) as unknown as Record<string, unknown>

    if (code === 'request.invalid') return t('redirects.errors.invalid')
    if (code === 'identity.forbidden') return t('redirects.errors.forbidden')
    if (code === 'pages.out_of_date') return t('redirects.errors.out_of_date')

    const key = code.startsWith('redirects.') ? `redirects.errors.${code.slice('redirects.'.length)}` : ''
    if (key && te(key)) {
      return t(key, { limit: Number(problem.limit ?? 10000) })
    }
  }

  return t('redirects.errors.unknown')
}

/** The redirect a refusal names (`redirects.duplicate`, `redirects.loop`), for a link to it. */
export function conflictingRedirectId(caught: unknown): string | null {
  if (!(caught instanceof ApiError)) return null
  const id = (caught.problem as unknown as Record<string, unknown> | undefined)?.redirectId
  return typeof id === 'string' ? id : null
}
