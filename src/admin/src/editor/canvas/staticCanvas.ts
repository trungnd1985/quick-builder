import type { InjectionKey } from 'vue'

/**
 * Provided as true inside content that is shown but not part of the document being edited —
 * a shared component on a page. Nodes there carry no ids and take no selection or edits.
 */
export const STATIC_CANVAS: InjectionKey<boolean> = Symbol('qb-static-canvas')
