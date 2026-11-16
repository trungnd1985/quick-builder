import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { ApiError } from '../../../src/api/request'
import { deleteLockHeldMessage } from '../../../src/features/trash/lockHeld'
import en from '../../../src/i18n/en.json'

// 021 FR-008: a delete refused because someone else is editing names them.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en }, datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric' } } } })
const { t, d } = i18n.global

function refusal(errorCode: string, extra: object = {}): ApiError {
  return new ApiError('refused', 409, { title: 'Conflict', status: 409, errorCode, ...extra })
}

describe('deleteLockHeldMessage', () => {
  it('names who is editing', () => {
    const message = deleteLockHeldMessage(t, d, refusal('editing.lock_held', { holder: { id: 'u', displayName: 'Dana' }, since: '2026-11-01T10:00:00Z' }))
    expect(message).toContain('Dana')
  })

  it('leaves every other refusal alone', () => {
    expect(deleteLockHeldMessage(t, d, refusal('pages.has_children'))).toBeNull()
    expect(deleteLockHeldMessage(t, d, new Error('boom'))).toBeNull()
  })
})
