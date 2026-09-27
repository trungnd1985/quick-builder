/**
 * Suggests a slug from a page title (010 FR-005, research P-04).
 *
 * A typing convenience only: the server's rule is the slug grammar, and a slug someone
 * types is sent as typed and refused if it breaks the grammar, never silently rewritten.
 * The suggestion always satisfies the grammar, or is empty when the title has nothing to
 * build one from.
 */

/** Letters Unicode normalisation cannot take apart into a base letter and a mark. */
const FOLDED: Record<string, string> = {
  đ: 'd',
  ð: 'd',
  ø: 'o',
  ß: 'ss',
  æ: 'ae',
  œ: 'oe',
  ł: 'l',
  þ: 'th',
  ı: 'i',
}

export const MAX_SLUG_LENGTH = 100

export function slugify(title: string): string {
  const folded = title
    .normalize('NFD')
    .replace(/\p{M}+/gu, '')
    .toLowerCase()
    .replace(/[đðøßæœłþı]/g, (letter) => FOLDED[letter] ?? letter)

  let slug = folded
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')

  if (slug.length > MAX_SLUG_LENGTH) {
    const cut = slug.slice(0, MAX_SLUG_LENGTH + 1)
    const boundary = cut.lastIndexOf('-')
    slug = (boundary > 0 ? cut.slice(0, boundary) : cut.slice(0, MAX_SLUG_LENGTH)).replace(/-+$/g, '')
  }

  return slug
}

/** The server's slug grammar, for an early hint while typing. The server decides. */
export function isValidSlug(slug: string): boolean {
  return slug.length > 0 && slug.length <= MAX_SLUG_LENGTH && /^[a-z0-9]+(-[a-z0-9]+)*$/.test(slug)
}
