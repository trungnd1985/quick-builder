import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/request'

/**
 * Turns a refused transfer operation into a message for people (020 data-model §5).
 *
 * As for sites, the message comes from the translation keyed by the server's `errorCode`,
 * never from its English `detail`; an unknown code falls back to a generic message.
 */
export function transferErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    const key = `transfer.errors.${code.replace(/\./g, '_')}`
    if (code && te(key)) {
      return t(key)
    }

    if (code === 'identity.forbidden') {
      return t('transfer.errors.forbidden')
    }
  }

  return t('transfer.errors.unknown')
}
