import { createI18n } from 'vue-i18n'
import en from './en.json'

// FR-045a: every piece of visible text is looked up, never written into a screen.
//
// One locale ships in this feature and no translation process is set up. The point is
// not that the app is translated — it is that adding a language later never requires
// reopening the screens. Text baked into components is the single most expensive thing
// to undo once several modules have built on the pattern.
//
// The `no-raw-text` lint rule fails the build on any string that escapes this, so the
// convention is enforced rather than remembered (SC-013).
export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
})

export type MessageSchema = typeof en
