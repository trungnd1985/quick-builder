import { describe, expect, it } from 'vitest'
import { isValidSlug, slugify } from '../src/features/pages/slugify'

// 010 FR-005, research P-04: suggestions always satisfy the server's grammar, and letters
// with diacritics fold to their base letters.

describe('slugify', () => {
  it.each([
    ['Dịch vụ & Giá', 'dich-vu-gia'],
    ['Đà Nẵng', 'da-nang'],
    ['Straße', 'strasse'],
    ['Ærø Ø', 'aero-o'],
    ['Łódź', 'lodz'],
    ['  --Hello,  World!--', 'hello-world'],
    ['Über uns', 'uber-uns'],
    ['2026 Plans', '2026-plans'],
  ])('suggests %s → %s', (title, slug) => {
    expect(slugify(title)).toBe(slug)
    expect(isValidSlug(slugify(title))).toBe(true)
  })

  it('gives no suggestion for a title with nothing to build one from', () => {
    expect(slugify('🎉🎉')).toBe('')
    expect(slugify('   ')).toBe('')
  })

  it('cuts a long title at a hyphen, within 100 characters', () => {
    const slug = slugify('word '.repeat(40))

    expect(slug.length).toBeLessThanOrEqual(100)
    expect(slug.endsWith('-')).toBe(false)
    expect(isValidSlug(slug)).toBe(true)
  })

  it.each(['About', '-a', 'a-', 'a--b', 'a_b', '', 'x'.repeat(101)])('refuses %j as a slug', (slug) => {
    expect(isValidSlug(slug)).toBe(false)
  })
})
