// The schema of the collection a Collection list shows, for its filter and sort controls (016
// US4): its own collection, or the target of the multi-reference field it reads. Loaded once.

import { computed, watch, type ComputedRef } from 'vue'
import type { SchemaInfo } from '../../bindings/bindingRules'
import { listCollection, scopesAt } from '../../bindings/scopes'
import type { ContentNode } from '../../schema/schema'
import { useEditorStore } from '../../state/editorStore'

export function useListSourceSchema(list: () => ContentNode | null): ComputedRef<SchemaInfo | null> {
  const store = useEditorStore()

  const collectionId = computed(() => {
    const node = list()
    if (!node || !store.root) return null
    const outer = scopesAt(store.root, node.id, store.bindings.templateCollectionId, store.bindings.schemas)
    return listCollection(node, outer, store.bindings.schemas)
  })

  watch(collectionId, (id) => {
    if (id) void store.bindings.schemaOf(id)
  }, { immediate: true })

  return computed(() => (collectionId.value ? store.bindings.schemas[collectionId.value] ?? null : null))
}
