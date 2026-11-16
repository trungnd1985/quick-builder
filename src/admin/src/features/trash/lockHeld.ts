// 021 FR-008, research TR-09: a delete is refused while another member is editing the document.
// The refusal names who and since when, in 023's shape (holder, since).
import type { Composer } from 'vue-i18n'
import { ApiError } from '../../api/request'

/** The message for an `editing.lock_held` refusal of a delete, or null for any other error. */
export function deleteLockHeldMessage(t: Composer['t'], d: Composer['d'], caught: unknown): string | null {
  if (!(caught instanceof ApiError) || caught.errorCode !== 'editing.lock_held') return null
  const problem = (caught.problem ?? {}) as unknown as { holder?: { displayName?: string }; since?: string }
  const name = problem.holder?.displayName || t('trash.lockHeld.someone')
  const since = problem.since ? d(new Date(problem.since), 'short') : ''
  return t('trash.lockHeld.message', { name, since })
}
