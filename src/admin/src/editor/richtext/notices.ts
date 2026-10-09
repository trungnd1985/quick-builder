// What a paste or drop left out, in words (035 FR-012, FR-026), for the toolbar's status line.

import type { Composer } from 'vue-i18n'
import type { RichTextNotice } from './paste'

export function noticeText(t: Composer['t'], te: Composer['te'], notice: RichTextNotice): string {
  if (notice.kind === 'imagesLeftOut') return t('editor.richText.notice.imagesLeftOut', notice.count)
  if (notice.kind === 'limit') return t(`editor.richText.notice.${notice.limit}Limit`)
  const key = notice.code?.startsWith('media.') ? `media.errors.${notice.code.slice('media.'.length)}` : ''
  return t('editor.richText.notice.uploadFailed', { reason: key && te(key) ? t(key) : t('media.errors.unknown') })
}
