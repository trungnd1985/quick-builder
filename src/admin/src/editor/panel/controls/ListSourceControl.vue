<script setup lang="ts">
// What a Collection list shows (016 US4, binding-expression.md §5): one of the site's
// collections, or, on a template page, the items one of the page item's multi-reference fields
// holds ("Related posts").
import { computed, onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { cmsApi, type CollectionSummary } from '../../../api/cms'
import { isObject, type Json } from '../../schema/subsetValidator'
import { fieldSources } from '../../bindings/scopes'
import { useEditorStore } from '../../state/editorStore'
import FieldShell from './FieldShell.vue'
import { describedBy, type ControlProps } from './types'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const collections = shallowRef<CollectionSummary[]>([])
onMounted(async () => {
  try {
    collections.value = (await cmsApi.list(props.siteId)).collections
  } catch {
    // Without the list, the current source still shows below.
  }
})

/** The multi-reference fields of the scopes around the list, nearest first (016 US6). */
const sources = computed(() =>
  store.selectedNode && store.root
    ? fieldSources(store.root, store.selectedNode, store.bindings.templateCollectionId, store.bindings.schemas)
    : [])

const sourceKey = (scope: string, fieldId: string) => `field:${scope}:${fieldId}`

const current = computed(() => {
  const source = props.value
  if (!isObject(source)) return ''
  if (typeof source.collectionId === 'string') return `collection:${source.collectionId}`
  if (typeof source.field === 'string') {
    // A source stored without a scope reads the nearest one.
    const scope = typeof source.scope === 'string' ? source.scope : sources.value[0]?.scope.name ?? 'page'
    return sourceKey(scope, source.field)
  }
  return ''
})

const scopeLabel = (name: string, label: string | null) =>
  name === 'page' ? t('editor.list.scopePage') : t('editor.list.scopeCard', { list: label ?? name })

/** A source no longer offered: a deleted collection or field. */
const unknown = computed(() =>
  current.value !== ''
  && !collections.value.some((c) => current.value === `collection:${c.id}`)
  && !sources.value.some((s) => current.value === sourceKey(s.scope.name, s.fieldId)))

function onChange(event: Event): void {
  const [kind, first, second] = (event.target as HTMLSelectElement).value.split(':')
  if (kind === 'collection') emit('change', { collectionId: first })
  else if (kind === 'field') emit('change', { field: second, scope: first })
  else emit('change', {})
}
</script>

<template>
  <FieldShell
    :id="id"
    :prop="prop"
    :label="label"
    :error="error"
  >
    <select
      :id="id"
      :value="current"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy(id, error)"
      @change="onChange"
    >
      <option value="">
        {{ t('editor.list.source.none') }}
      </option>
      <option
        v-if="unknown"
        :value="current"
      >
        {{ t('editor.list.source.unknown') }}
      </option>
      <optgroup :label="t('editor.list.source.collections')">
        <option
          v-for="collection in collections"
          :key="collection.id"
          :value="`collection:${collection.id}`"
        >
          {{ collection.name }}
        </option>
      </optgroup>
      <optgroup
        v-if="sources.length > 0"
        :label="t('editor.list.source.scopeItems')"
      >
        <option
          v-for="source in sources"
          :key="sourceKey(source.scope.name, source.fieldId)"
          :value="sourceKey(source.scope.name, source.fieldId)"
        >
          {{ t('editor.list.source.fieldOf', { field: source.fieldName, where: scopeLabel(source.scope.name, source.scope.label) }) }}
        </option>
      </optgroup>
    </select>
  </FieldShell>
</template>
