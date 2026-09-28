// The editor's half of each block type's single definition (011 research ED-08).
//
// The schema holds what a block is: its properties, their limits and defaults, where it may
// sit. A block's folder holds only what the editor needs beyond that — its palette entry,
// how its properties are grouped and edited, what a new one starts with, and how the canvas
// draws it. BlockRegistryCoverage.spec.ts fails the build if this and the schema disagree.

import type { Component } from 'vue'
import type { Json } from '../schema/subsetValidator'
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

export interface PropGroup {
  /** An i18n key under `editor.panel.groups`. */
  labelKey: string
  props: { prop: string; control: ControlKind }[]
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
  initialProps: (t: (key: string) => string) => Record<string, Json>
  /** The canvas component. It receives the node as its `node` prop. */
  component: Component
  /** The property edited in place on the canvas, if any (FR-019). */
  inlineText?: string
}

const definitions: BlockDefinition[] = [section, container, heading, text, richText, image, button, spacer, divider, embed, componentRef]

export const registry: Readonly<Record<string, BlockDefinition>> = Object.fromEntries(definitions.map((d) => [d.type, d]))

/** The definition for a type, or undefined for one the editor does not draw. */
export const definitionOf = (type: string): BlockDefinition | undefined => registry[type]
