import { describe, expect, it } from 'vitest'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'
import { isDeletedToken, tokenCss, tokenValue } from '../../src/editor/tokens/tokenCss'

// 011 FR-033, clarification Q4, data-model §2 "Fallbacks always resolve".

const clone = (): TokenSet => JSON.parse(JSON.stringify(defaultTokens)) as TokenSet

describe('tokenCss', () => {
  it('turns every token into a custom property', () => {
    const css = tokenCss(defaultTokens)

    expect(css['--qb-color-color-brand']).toBe('#2563eb')
    expect(css['--qb-text-text-lg']).toBe('calc(var(--qb-base-size) * 1.25)')
    expect(css['--qb-space-space-md']).toBe('calc(var(--qb-base-size) * 1)')
    expect(css['--qb-base-size']).toBe('16px')
    expect(css['--qb-font-body']).toBe('system-ui, sans-serif')
  })

  it('uses the site value over the default', () => {
    const set = clone()
    set.colors[0].value = '#00ff00'
    set.typography.headingFont = 'didone'

    expect(tokenCss(set)['--qb-color-color-brand']).toBe('#00ff00')
    expect(tokenCss(set)['--qb-font-heading']).toContain('Didot')
  })

  it('still defines default ids a site deleted, so fallbacks resolve', () => {
    const set = clone()
    set.colors = set.colors.filter((c) => c.id !== 'color-text')
    set.typography.sizes = set.typography.sizes.filter((s) => s.id !== 'text-3xl')

    const css = tokenCss(set)

    expect(css['--qb-color-color-text']).toBe('#111827')
    expect(css['--qb-text-text-3xl']).toBe('calc(var(--qb-base-size) * 2.5)')
  })
})

describe('tokenValue', () => {
  it('refers to the token by variable, so a value change reaches every block', () => {
    expect(tokenValue({ token: 'color-brand' }, 'color', defaultTokens, { token: 'color-text' })).toBe('var(--qb-color-color-brand)')
  })

  it('falls back to the property default when the token was deleted', () => {
    expect(tokenValue({ token: 'color-gone' }, 'color', defaultTokens, { token: 'color-text' })).toBe('var(--qb-color-color-text)')
    expect(isDeletedToken({ token: 'color-gone' }, 'color', defaultTokens)).toBe(true)
    expect(isDeletedToken({ token: 'color-brand' }, 'color', defaultTokens)).toBe(false)
  })

  it('uses the given fallback for a missing reference without a default', () => {
    expect(tokenValue(undefined, 'color', defaultTokens, undefined, 'transparent')).toBe('transparent')
  })
})
