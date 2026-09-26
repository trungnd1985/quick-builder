import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/sites'

/**
 * Turns a refused site operation into a message for people (009 FR-006).
 *
 * The server's `errorCode` is the stable part of the contract, so the message comes from
 * the translation lookup keyed by it, never from the server's English `detail`. A code
 * this screen does not know yet falls back to a generic message rather than showing
 * nothing.
 */
export function siteErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    const key = code.startsWith('sites.') ? `sites.errors.${code.slice('sites.'.length)}` : ''
    if (key && te(key)) {
      return t(key)
    }

    if (code === 'identity.forbidden') {
      return t('sites.errors.forbidden')
    }
  }

  return t('sites.errors.unknown')
}

/** Whether a refusal means "reload and try again" rather than "fix your input". */
export function isOutOfDate(caught: unknown): boolean {
  return caught instanceof ApiError && caught.errorCode === 'sites.out_of_date'
}
