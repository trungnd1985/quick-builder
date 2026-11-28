import { describe, expect, it } from 'vitest'
import { borderStyle } from '../../src/editor/blocks/useBorder'
import { defaultTokens } from '../../src/editor/tokens/defaultTokens'
import type { ContentNode } from '../../src/editor/schema/schema'
import type { Json } from '../../src/editor/schema/subsetValidator'

// 066 research BB-04: the same cases as BorderStyleTests.cs, so the canvas and the published
// page frame a block identically.

const node = (props: Record<string, Json>): ContentNode => ({ id: 'b', type: 'text', props, children: [] })
const of = (props: Record<string, Json>) => {
  const { classes, style } = borderStyle(node(props), defaultTokens)
  return { classes: classes.join(' '), style }
}

describe('borderStyle', () => {
  it('gives a block without a border nothing', () => {
    expect(of({})).toEqual({ classes: '', style: {} })
  })

  it.each([
    ['thin', 'solid'],
    ['thin', 'dashed'],
    ['medium', 'dotted'],
    ['thick', 'solid'],
  ])('turns width %s and style %s into classes and the colour into a value', (width, style) => {
    expect(of({ border: { width, style, color: { token: 'color-brand' } } })).toEqual({
      classes: `qb-border qb-border--${width} qb-border--${style}`,
      style: { '--qb-border-color': 'var(--qb-color-color-brand)' },
    })
  })

  it('takes the style and colour defaults from the schema', () => {
    expect(of({ border: { width: 'thin' } })).toEqual({
      classes: 'qb-border qb-border--thin qb-border--solid',
      style: { '--qb-border-color': 'var(--qb-color-color-border)' },
    })
  })

  it('draws no line and writes no colour at width none', () => {
    expect(of({ border: { width: 'none', style: 'dashed', color: { token: 'color-brand' } } })).toEqual({ classes: 'qb-border', style: {} })
  })

  it('falls back to the default colour for a token that is gone', () => {
    expect(of({ border: { width: 'thin', color: { token: 'color-gone' } } }).style).toEqual({ '--qb-border-color': 'var(--qb-color-color-border)' })
  })

  it('rounds the corners with a radius that exists, even without a line', () => {
    expect(of({ border: { radius: { token: 'radius-md' } } })).toEqual({
      classes: 'qb-border qb-border--radius',
      style: { '--qb-border-radius': 'var(--qb-radius-radius-md)' },
    })
  })

  it('leaves the corners alone for a radius that is gone', () => {
    expect(of({ border: { radius: { token: 'radius-gone' } } })).toEqual({ classes: 'qb-border', style: {} })
  })

  it('moves the line off the content with padding that exists', () => {
    expect(of({ border: { width: 'thin', padding: { token: 'space-sm' } } })).toEqual({
      classes: 'qb-border qb-border--thin qb-border--solid qb-border--pad',
      style: { '--qb-border-color': 'var(--qb-color-color-border)', '--qb-border-pad': 'var(--qb-space-space-sm)' },
    })
  })

  it('leaves the spacing alone for padding that is gone', () => {
    expect(of({ border: { padding: { token: 'space-gone' } } })).toEqual({ classes: 'qb-border', style: {} })
  })

  it('puts every part in contract order', () => {
    const result = of({
      border: { width: 'medium', style: 'dotted', color: { token: 'color-text' }, radius: { token: 'radius-lg' }, padding: { token: 'space-md' } },
    })
    expect(result.classes).toBe('qb-border qb-border--medium qb-border--dotted qb-border--radius qb-border--pad')
    expect(Object.entries(result.style)).toEqual([
      ['--qb-border-color', 'var(--qb-color-color-text)'],
      ['--qb-border-radius', 'var(--qb-radius-radius-lg)'],
      ['--qb-border-pad', 'var(--qb-space-space-md)'],
    ])
  })

  it.each([['thin'], [[]], [null]])('treats a border of %j as absent', (value) => {
    expect(of({ border: value as Json })).toEqual({ classes: '', style: {} })
  })

  it('never lets a value outside the enums reach a class name', () => {
    expect(of({ border: { width: '"><script>', style: '"><b>' } }).classes).toBe('qb-border')
  })

  it('uses the default radii for a set saved before they existed', () => {
    const old = JSON.parse(JSON.stringify(defaultTokens)) as typeof defaultTokens
    delete (old as { radii?: unknown }).radii
    expect(borderStyle(node({ border: { radius: { token: 'radius-sm' } } }), old).style).toEqual({ '--qb-border-radius': 'var(--qb-radius-radius-sm)' })
  })
})
