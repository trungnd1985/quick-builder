import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/request'

/**
 * Turns a refused submissions operation into a message for people (018 forms-api.yaml). The
 * message comes from the translation keyed by the server's `errorCode`; a filter refusal adds
 * the server's reason, which names the filter at fault.
 */
export function formsErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    if (code === 'identity.forbidden') return t('forms.errors.forbidden')
    const key = code.startsWith('forms.') ? `forms.errors.${code.slice('forms.'.length)}` : ''
    if (key && te(key)) return t(key, { reason: caught.message })
  }

  return t('forms.errors.generic')
}
