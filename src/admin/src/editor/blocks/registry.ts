// The editor's half of each block type's single definition (011 research ED-08).
//
// The schema holds what a block is: its properties, their limits and defaults, where it may
// sit. A block's folder holds only what the editor needs beyond that — its palette entry,
// how its properties are grouped and edited, what a new one starts with, and how the canvas
// draws it. BlockRegistryCoverage.spec.ts fails the build if this and the schema disagree.

import type { Component } from 'vue'
import type { Json } from '../schema/subsetValidator'
import { blockTypes, type ContentNode } from '../schema/schema'
import section from './section/definition'
import container from './container/definition'
import heading from './heading/definition'
import text from './text/definition'
import richText from './rich-text/definition'
import image from './image/definition'
import button from './button/definition'
import spacer from './spacer/definition'
import divider from './divider/definition'
import embed from './embed/definition'
import componentRef from './component-ref'
import collectionList from './collection-list/definition'
import listCard from './list-card/definition'
import listEmpty from './list-empty/definition'
import navigation from './navigation/definition'
import form from './form/definition'

/** How the property panel edits a property (US2). */
export type ControlKind =
  | 'text'
  | 'multiline'
  | 'choice'
  | 'toggle'
  | 'number'
  | 'colorToken'
  | 'spaceToken'
  | 'textSizeToken'
  | 'link'
  | 'media'
  | 'richText'
  | 'embedUrl'
  // 016 research TB-14: the Collection list's source, filters, sort keys and paging.
  | 'listSource'
  | 'listFilters'
  | 'listSort'
  | 'listPaging'
  // 024 research NV-16: the Navigation block's menu and its collapsing.
  | 'menu'
  | 'navCollapse'
  // 024 FR-017a: a Navigation block's look and colours, unset following the site's menu style.
  | 'menuLook'
  | 'menuColor'
  // 018: a form's fields, where it sends visitors, who is told, and how long submissions stay.
  | 'formFields'
  | 'formRedirect'
  | 'formNotify'
  | 'formRetention'

export interface PropGroup {
  /** An i18n key under `editor.panel.groups`. */
  labelKey: string
  props: { prop: string; control: ControlKind }[]
}

/** What a new block may start from beyond its type: the site's collections, in list order (016). */
export interface InsertContext {
  collectionIds: readonly string[]
  /** The site's menus, in name order: a new Navigation block starts with the first (024). */
  menuIds?: readonly string[]
  /**
   * The name field of a collection whose schema the editor has loaded, null when it has none
   * or is not loaded: a new list's card shows each item's name (016 FR-022).
   */
  nameFieldOf?: (collectionId: string) => string | null
}

export interface BlockDefinition {
  type: string
  /** An i18n key for the block's name in the palette, layer view and panel. */
  labelKey: string
  /** A short glyph for the palette and layer view; decorative. */
  icon: string
  /** The property panel's groups. `layerLabel` is added to every block by the panel itself. */
  groups: PropGroup[]
  /** What a new block holds: its required properties only (data-model §1.2). */
  initialProps: (t: (key: string) => string, context: InsertContext) => Record<string, Json> | null
  /**
   * The blocks a new block starts with, when it is never empty: a list's card and empty state
   * (016). `initialProps` returns null when the block cannot be made here: a list on a site
   * without collections.
   */
  initialChildren?: (t: (key: string) => string, context: InsertContext, props: Record<string, Json>) => ContentNode[]
  /** The canvas component. It receives the node as its `node` prop. */
  component: Component
  /** The property edited in place on the canvas, if any (FR-019). */
  inlineText?: string
  /**
   * False for a type the palette never offers: the list's card and empty state, which exist
   * only inside their list and arrive with it (016 TB-07).
   */
  palette?: false
  /**
   * Why a block cannot be made when `initialProps` returns null: a list needs a collection (016),
   * a Navigation block a menu (024).
   */
  missing?: 'no_collections' | 'no_menus'
}

const definitions: BlockDefinition[] = [
  section, container, heading, text, richText, image, button, spacer, divider, embed, componentRef, collectionList, listCard, listEmpty, navigation, form,
]

export const registry: Readonly<Record<string, BlockDefinition>> = Object.fromEntries(definitions.map((d) => [d.type, d]))

/**
 * The block types the palette offers and inserts on their own (016 TB-07): every block type
 * but the Collection list, which is inserted with its card and empty state.
 */
export const insertableTypes: readonly string[] = blockTypes.filter((type) => registry[type]?.palette !== false)

/** The definition for a type, or undefined for one the editor does not draw. */
export const definitionOf = (type: string): BlockDefinition | undefined => registry[type]

/**
 * A new block of `type`: its initial properties and children, or why it cannot be made (a
 * Collection list on a site with no collections). The palette, drag and drop and the keyboard
 * all make blocks through here.
 */
export function newBlock(
  type: string,
  t: (key: string) => string,
  context: InsertContext,
  initialProps?: Record<string, Json>,
): { props: Record<string, Json>; children: ContentNode[] } | { refused: 'no_collections' | 'no_menus' | 'not_found' } {
  const definition = registry[type]
  if (!definition) return { refused: 'not_found' }
  const props = initialProps ?? definition.initialProps(t, context)
  if (!props) return { refused: definition.missing ?? 'no_collections' }
  return { props, children: definition.initialChildren?.(t, context, props) ?? [] }
}
