<script setup lang="ts">
// A Collection list's order (016 US4, binding-expression.md §5): up to three sort keys, most
// significant first, each a field that orders or a built-in item value, ascending or
// descending. Items without a value come last either way; the item id breaks ties.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ITEM_VALUES, MAX_SORTS, sortable, targetOf } from '../../bindings/listRules'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import { useListSourceSchema } from './listSourceSchema'
import type { ControlProps } from './types'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const schema = useListSourceSchema(() => store.selectedNode)
const sorts = computed<JsonObject[]>(() => (Array.isArray(props.value) ? props.value.filter(isObject) : []))

const targets = computed(() => [
  ...(schema.value
    ? schema.value.order
      .filter((id) => sortable({ field: schema.value!.fields[id] }))
      .map((id) => ({ key: `field:${id}`, label: schema.value!.fields[id].name }))
    : []),
  ...ITEM_VALUES.map((item) => ({ key: `item:${item}`, label: t(`editor.list.itemValues.${item}`) })),
])

const keyOf = (sort: JsonObject): string =>
  typeof sort.field === 'string' ? `field:${sort.field}` : typeof sort.item === 'string' ? `item:${sort.item}` : ''

const broken = (sort: JsonObject): boolean => {
  const target = targetOf(sort, schema.value)
  return !target || !sortable(target)
}

function emitWith(index: number, next: JsonObject | null): void {
  const all = [...sorts.value]
  if (next) all[index] = next
  else all.splice(index, 1)
  emit('change', all)
}

function onTarget(index: number, event: Event): void {
  const [kind, id] = (event.target as HTMLSelectElement).value.split(':')
  emitWith(index, { ...(kind === 'field' ? { field: id } : { item: id }), dir: sorts.value[index].dir ?? 'asc' })
}

const rowId = (index: number, part: string) => `${props.id}-${index}-${part}`
</script>

<template>
  <fieldset
    class="editor-field editor-list-rows"
    :data-prop="prop"
  >
    <legend class="editor-field__label">
      {{ label }}
    </legend>
    <p
      v-if="sorts.length === 0"
      class="help"
    >
      {{ t('editor.list.noSort') }}
    </p>
    <div
      v-for="(sort, index) in sorts"
      :key="index"
      class="editor-list-row"
      :class="{ 'editor-list-row--broken': broken(sort) }"
      role="group"
      :aria-label="t('editor.list.sortN', { n: index + 1 })"
    >
      <label :for="rowId(index, 'target')">{{ t('editor.list.sortTarget') }}</label>
      <select
        :id="rowId(index, 'target')"
        :value="keyOf(sort)"
        @change="onTarget(index, $event)"
      >
        <option
          v-if="broken(sort)"
          :value="keyOf(sort)"
        >
          {{ t('editor.list.unknownField') }}
        </option>
        <option
          v-for="target in targets"
          :key="target.key"
          :value="target.key"
        >
          {{ target.label }}
        </option>
      </select>
      <label :for="rowId(index, 'dir')">{{ t('editor.list.sortDirection') }}</label>
      <select
        :id="rowId(index, 'dir')"
        :value="String(sort.dir ?? 'asc')"
        @change="emitWith(index, { ...sort, dir: ($event.target as HTMLSelectElement).value })"
      >
        <option value="asc">
          {{ t('editor.list.ascending') }}
        </option>
        <option value="desc">
          {{ t('editor.list.descending') }}
        </option>
      </select>
      <p
        v-if="broken(sort)"
        class="field-error"
      >
        {{ t('editor.list.sortBroken') }}
      </p>
      <button
        type="button"
        class="button button--small"
        @click="emitWith(index, null)"
      >
        {{ t('editor.list.removeSort', { n: index + 1 }) }}
      </button>
    </div>
    <button
      type="button"
      class="button button--small"
      :disabled="sorts.length >= MAX_SORTS"
      @click="emit('change', [...sorts, { item: 'firstPublishedAt', dir: 'desc' }])"
    >
      {{ t('editor.list.addSort') }}
    </button>
  </fieldset>
</template>
