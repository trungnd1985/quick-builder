import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/cms'

/**
 * Turns a refused collection operation into a message for people (014 data-model §5).
 *
 * The message comes from the translation lookup keyed by the server's `errorCode` and, for a
 * schema problem, its `rule` — never from its English `detail`. An unknown code falls back to
 * a generic message.
 */
export function cmsErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''

    if (code === 'identity.forbidden') {
      return t('cms.errors.forbidden')
    }

    const key = code.startsWith('cms.') ? `cms.errors.${code.slice('cms.'.length)}` : ''
    if (key && te(key)) {
      return t(key, { reason: ruleMessage(t, te, caught.problem?.rule) })
    }
  }

  return t('cms.errors.unknown')
}

/** The reason for a schema rule, from contract §5. */
export function ruleMessage(t: Composer['t'], te: Composer['te'], rule: string | undefined): string {
  const key = `cms.rules.${rule ?? ''}`
  return rule && te(key) ? t(key) : t('cms.rules.schema_violation')
}

export const errorCodeOf = (caught: unknown): string | undefined => (caught instanceof ApiError ? caught.errorCode : undefined)
