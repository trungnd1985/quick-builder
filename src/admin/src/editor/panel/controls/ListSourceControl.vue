<script setup lang="ts">
// What a Collection list shows (016 US4, binding-expression.md §5): one of the site's
// collections, or, on a template page, the items one of the page item's multi-reference fields
// holds ("Related posts").
import { computed, onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { cmsApi, type CollectionSummary } from '../../../api/cms'
import { isObject, type Json } from '../../schema/subsetValidator'
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

/** The page item's multi-reference fields, on a template page. */
const fieldSources = computed(() => {
  const template = store.bindings.templateCollectionId
  const schema = template ? store.bindings.schemas[template] : undefined
  return schema ? schema.order.map((id) => schema.fields[id]).filter((f) => f.type === 'multiReference') : []
})

const current = computed(() => {
  const source = props.value
  if (!isObject(source)) return ''
  if (typeof source.collectionId === 'string') return `collection:${source.collectionId}`
  if (typeof source.field === 'string') return `field:${source.field}`
  return ''
})

/** A source no longer offered: a deleted collection or field. */
const unknown = computed(() =>
  current.value !== ''
  && !collections.value.some((c) => current.value === `collection:${c.id}`)
  && !fieldSources.value.some((f) => current.value === `field:${f.id}`))

function onChange(event: Event): void {
  const [kind, id] = (event.target as HTMLSelectElement).value.split(':')
  if (kind === 'collection') emit('change', { collectionId: id })
  else if (kind === 'field') emit('change', { field: id })
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
        v-if="fieldSources.length > 0"
        :label="t('editor.list.source.pageItem')"
      >
        <option
          v-for="field in fieldSources"
          :key="field.id"
          :value="`field:${field.id}`"
        >
          {{ field.name }}
        </option>
      </optgroup>
    </select>
  </FieldShell>
</template>
