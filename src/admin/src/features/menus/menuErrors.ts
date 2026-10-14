import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/request'

/**
 * Turns a refused menu operation into a message for people (024 data-model §4.2).
 *
 * The message comes from the translation lookup keyed by the server's `errorCode`, never from
 * its English `detail`. A document refusal carries the broken `rule` and its `pointer`, which
 * name the entry; the rule picks the message. A code this screen does not know falls back to a
 * generic message.
 */
export function menuErrorMessage(t: Composer['t'], te: Composer['te'], caught: unknown): string {
  if (caught instanceof ApiError) {
    const code = caught.errorCode ?? ''
    const problem = (caught.problem ?? {}) as unknown as Record<string, unknown>

    if (code === 'identity.forbidden') return t('menus.errors.forbidden')
    if (code === 'pages.out_of_date') return t('menus.errors.out_of_date')

    if (code === 'pages.invalid_menu_document' && typeof problem.rule === 'string') {
      const ruleKey = `menus.errors.rules.${problem.rule}`
      return te(ruleKey) ? t(ruleKey) : t('menus.errors.invalid_menu_document')
    }

    const key = code.startsWith('pages.') ? `menus.errors.${code.slice('pages.'.length)}` : ''
    if (key && te(key)) {
      return t(key, {
        limit: Number(problem.limit ?? 50),
        pages: Number(problem.pages ?? 0),
        components: Number(problem.components ?? 0),
      })
    }
  }

  return t('menus.errors.unknown')
}

/** The JSON pointer of the entry a document refusal names, if any (`/entries/0/children/2`). */
export function refusedEntryPointer(caught: unknown): string | null {
  if (!(caught instanceof ApiError) || caught.errorCode !== 'pages.invalid_menu_document') return null
  const pointer = (caught.problem as unknown as Record<string, unknown> | undefined)?.pointer
  if (typeof pointer !== 'string') return null
  const match = /^((?:\/entries\/\d+)(?:\/children\/\d+)*)/.exec(pointer)
  return match ? match[1] : null
}
