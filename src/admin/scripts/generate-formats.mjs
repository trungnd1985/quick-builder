// Writes docs/contracts/binding-expression/formats.json: how bound dates and numbers read in
// each site language (016 research TB-09; binding-expression.md §4).
//
// The server runs with invariant globalization, so it has no locale data; the editor has the
// browser's. Neither is used at run time. Instead this script reads Node's ICU once, when a
// language is added, and both sides format from the file it writes, so the canvas and the
// published page show the same text.
//
// Run from src/admin: node scripts/generate-formats.mjs
// The languages are the site languages: one per public-texts file of the Rendering module.

import { readdirSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '../../..')
const textsDir = join(root, 'src/Modules/Rendering/QuickBuilder.Rendering/Resources/public-texts')
const out = join(root, 'docs/contracts/binding-expression/formats.json')

const languages = readdirSync(textsDir).filter((f) => f.endsWith('.json')).map((f) => f.slice(0, -5)).sort()

// A day and month that can never be confused (day 23, month 11), in a year with 4 digits.
const sample = new Date(Date.UTC(2026, 10, 23))

/** A date pattern as the contract writes it: {d} {dd} {M} {MM} {MMM} {MMMM} {yyyy} and literal text. */
function pattern(language, options) {
  return new Intl.DateTimeFormat(language, { ...options, timeZone: 'UTC' })
    .formatToParts(sample)
    .map((part) => {
      switch (part.type) {
        case 'day': return options.day === '2-digit' ? '{dd}' : '{d}'
        case 'month':
          if (options.month === 'short') return '{MMM}'
          if (options.month === 'long') return '{MMMM}'
          return options.month === '2-digit' ? '{MM}' : '{M}'
        case 'year': return '{yyyy}'
        case 'literal': return part.value.replace(/[{}]/g, '')
        default: return part.value
      }
    })
    .join('')
}

/** The month names as they read inside a date, January first. */
function months(language, style) {
  return Array.from({ length: 12 }, (_, m) =>
    new Intl.DateTimeFormat(language, { day: 'numeric', month: style, year: 'numeric', timeZone: 'UTC' })
      .formatToParts(new Date(Date.UTC(2026, m, 15)))
      .find((p) => p.type === 'month').value)
}

function separators(language) {
  const parts = new Intl.NumberFormat(language, { useGrouping: true, minimumFractionDigits: 1 }).formatToParts(1234567.5)
  return {
    decimal: parts.find((p) => p.type === 'decimal')?.value ?? '.',
    group: parts.find((p) => p.type === 'group')?.value ?? ',',
  }
}

const formats = {
  $comment:
    'How bound dates and numbers read in each site language (binding-expression.md §4). Generated from ICU by src/admin/scripts/generate-formats.mjs; do not edit by hand. Patterns use {d} {dd} {M} {MM} {MMM} {MMMM} {yyyy}; everything else is literal.',
}

for (const language of languages) {
  formats[language] = {
    short: pattern(language, { day: '2-digit', month: '2-digit', year: 'numeric' }),
    medium: pattern(language, { day: 'numeric', month: 'short', year: 'numeric' }),
    long: pattern(language, { day: 'numeric', month: 'long', year: 'numeric' }),
    monthsShort: months(language, 'short'),
    monthsLong: months(language, 'long'),
    ...separators(language),
  }
}

writeFileSync(out, JSON.stringify(formats, null, 2) + '\n')
console.log(`Wrote ${languages.length} languages to ${out}`)
