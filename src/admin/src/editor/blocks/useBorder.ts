// The border every block shares, on the canvas (066 research BB-04, contracts/block-border.md §3):
// classes for the fixed widths and styles, and token-only custom properties for the colour, the
// corners and the inner spacing.
//
// Every canvas component appends `classes` after its own and merges `style` into its own; none
// writes border CSS of its own. BorderStyle.cs produces the same output on published pages, and
// useBorder.spec.ts and BorderStyleTests hold both to the same cases. Defaults come from
// `$defs/border` in the schema; `borderBox` has the same ones, without padding.

import { computed } from 'vue'
import { definitionDefault, type ContentNode } from '../schema/schema'
import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'
import type { TokenSet } from '../tokens/defaultTokens'
import { tokenValue, tokenValueIfSet } from '../tokens/tokenCss'
import { useEditorStore } from '../state/editorStore'

const DEFINITION = 'border'
const WIDTHS = ['none', 'thin', 'medium', 'thick'] as const
const STYLES = ['solid', 'dashed', 'dotted'] as const

export interface BorderStyle {
  classes: string[]
  style: Record<string, string>
}

/** The block's own `border` object; a value of any other kind counts as absent. */
const borderOf = (node: ContentNode): JsonObject | null => (isObject(node.props.border) ? node.props.border : null)

/** Whether the block has a border object at all: the two optional wrappers depend on it (BB-05). */
export const hasBorder = (node: ContentNode): boolean => borderOf(node) !== null

// An enum value becomes a class name, so only an allowed one passes: the value, else the schema
// default, else the first allowed (BorderStyle.Choice).
function choice(border: JsonObject, name: string, allowed: readonly string[]): string {
  const value = border[name]
  if (typeof value === 'string' && allowed.includes(value)) return value
  const fallback = definitionDefault(DEFINITION, name)
  return typeof fallback === 'string' && allowed.includes(fallback) ? fallback : allowed[0]
}

/** The border classes and custom properties for a node, against a token set. */
export function borderStyle(node: ContentNode, tokens: Readonly<TokenSet>): BorderStyle {
  const border = borderOf(node)
  if (!border) return { classes: [], style: {} }

  const classes = ['qb-border']
  const style: Record<string, string> = {}

  const width = choice(border, 'width', WIDTHS)
  if (width !== 'none') {
    classes.push(`qb-border--${width}`, `qb-border--${choice(border, 'style', STYLES)}`)
    style['--qb-border-color'] = tokenValue(border.color, 'color', tokens, definitionDefault(DEFINITION, 'color'))
  }

  // No default: absent, or naming a token the design lacks, the block keeps its own (BB-02).
  const radius = tokenValueIfSet(border.radius as Json | undefined, 'radius', tokens)
  if (radius) {
    classes.push('qb-border--radius')
    style['--qb-border-radius'] = radius
  }

  const padding = tokenValueIfSet(border.padding as Json | undefined, 'space', tokens)
  if (padding) {
    classes.push('qb-border--pad')
    style['--qb-border-pad'] = padding
  }

  return { classes, style }
}

/** The border of a canvas block, following the editor's tokens as they change. */
export function useBorder(node: () => ContentNode) {
  const store = useEditorStore()
  const border = computed(() => borderStyle(node(), store.tokens))
  return {
    classes: computed(() => border.value.classes),
    style: computed(() => border.value.style),
    has: computed(() => hasBorder(node())),
  }
}
