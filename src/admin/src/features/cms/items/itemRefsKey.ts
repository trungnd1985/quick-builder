import type { InjectionKey } from 'vue'
import type { ItemRefs } from './itemRefs'

/** The item form's cache of referenced items, provided by ItemFormView to every picker. */
export const ITEM_REFS: InjectionKey<ItemRefs> = Symbol('itemRefs')
