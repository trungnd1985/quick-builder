// How a bound date or number reads in the site's language (016 research TB-09).
//
// A port of the Rendering module's ValueFormatter. Both read the contract's formats.json — the
// patterns, month names and separators generated once from ICU — rather than the browser's or
// the server's locale data, so the canvas reads exactly as the published page.

import rawFormats from '@contracts/binding-expression/formats.json'

interface LanguageFormats {
  short: string
  medium: string
  long: string
  monthsShort: string[]
  monthsLong: string[]
  decimal: string
  group: string
}

const formats = rawFormats as unknown as Record<string, LanguageFormats>
const FALLBACK = 'en'

const forLanguage = (language: string): LanguageFormats => formats[language] ?? formats[FALLBACK]

const pad = (value: number, width: number): string => String(value).padStart(width, '0')

interface DateParts { year: number; month: number; day: number }

function apply(pattern: string, date: DateParts, f: LanguageFormats): string {
  return pattern.replace(/\{(d|dd|M|MM|MMM|MMMM|yyyy)\}/g, (_, token: string) => {
    switch (token) {
      case 'd': return String(date.day)
      case 'dd': return pad(date.day, 2)
      case 'M': return String(date.month)
      case 'MM': return pad(date.month, 2)
      case 'MMM': return f.monthsShort[date.month - 1]
      case 'MMMM': return f.monthsLong[date.month - 1]
      default: return pad(date.year, 4)
    }
  })
}

/** A date, and a time when it has one, in a style: short, medium (also when none), long or iso. */
export function formatDateParts(date: DateParts, time: { hour: number; minute: number } | null, style: string | undefined, language: string): string {
  if (style === 'iso') return `${pad(date.year, 4)}-${pad(date.month, 2)}-${pad(date.day, 2)}`
  const f = forLanguage(language)
  const pattern = style === 'short' ? f.short : style === 'long' ? f.long : f.medium
  const text = apply(pattern, date, f)
  return time ? `${text} ${pad(time.hour, 2)}:${pad(time.minute, 2)} UTC` : text
}

/** A date value (`YYYY-MM-DD` or `YYYY-MM-DDTHH:MM:SSZ`) in a style; null when it is neither. */
export function formatDate(value: string, style: string | undefined, language: string): string | null {
  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (dateOnly) return formatDateParts({ year: +dateOnly[1], month: +dateOnly[2], day: +dateOnly[3] }, null, style, language)
  const moment = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})Z$/.exec(value)
  if (moment) {
    return formatDateParts({ year: +moment[1], month: +moment[2], day: +moment[3] }, { hour: +moment[4], minute: +moment[5] }, style, language)
  }
  return null
}

/** A moment (an ISO time from the API) as a date: the built-in publish times. */
export function formatMoment(iso: string, style: string | undefined, language: string): string {
  const moment = new Date(iso)
  return formatDateParts(
    { year: moment.getUTCFullYear(), month: moment.getUTCMonth() + 1, day: moment.getUTCDate() },
    null,
    style,
    language,
  )
}

/** A number rounded to `decimals` places, half away from zero, grouped by thousands as the language groups them. */
export function formatNumber(value: number, decimals: number, language: string): string {
  const f = forLanguage(language)
  const places = Math.min(Math.max(decimals, 0), 6)
  // Shifting through the exponent keeps 1.005 at 1.01, as decimal rounding on the server does.
  const magnitude = Number(`${Math.round(Number(`${Math.abs(value)}e${places}`))}e-${places}`)
  const [whole, fraction = ''] = magnitude.toFixed(places).split('.')
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, f.group)
  const text = fraction ? `${grouped}${f.decimal}${fraction}` : grouped
  return value < 0 && magnitude !== 0 ? `-${text}` : text
}
