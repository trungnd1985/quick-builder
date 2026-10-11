<script setup lang="ts">
// A Collection list's filters (016 US4, binding-expression.md §5): up to ten conditions that must
// all hold. Each offers only the operators its field's type has, and a value input that fits:
// a literal, or on a template page a value of the page's item ("same category as this post",
// "not this post"). A filter that no longer fits the collection is marked: on the site it
// matches nothing.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FieldInfo } from '../../bindings/bindingRules'
import {
  ITEM_VALUES, MAX_FILTERS, MAX_TEXT_LITERAL, filterProblem, literalFor, operatorsFor, targetOf,
  type ListOperator, type ListTarget,
} from '../../bindings/listRules'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import { useListSourceSchema } from './listSourceSchema'
import type { ControlProps } from './types'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const list = computed(() => store.selectedNode)
const schema = useListSourceSchema(() => list.value)

const filters = computed<JsonObject[]>(() => (Array.isArray(props.value) ? props.value.filter(isObject) : []))

/** What each filter can read, in schema order, then the item's own values. */
const targets = computed(() => [
  ...(schema.value ? schema.value.order.map((id) => ({ key: `field:${id}`, label: schema.value!.fields[id].name })) : []),
  ...ITEM_VALUES.map((item) => ({ key: `item:${item}`, label: t(`editor.list.itemValues.${item}`) })),
])

/** The page item's fields, on a template page, for values read from it. */
const pageFields = computed<FieldInfo[]>(() => {
  const template = store.bindings.templateCollectionId
  const pageSchema = template ? store.bindings.schemas[template] : undefined
  return pageSchema ? pageSchema.order.map((id) => pageSchema.fields[id]) : []
})

const sourceIsPageCollection = computed(() =>
  !!schema.value && schema.value.collectionId === store.bindings.templateCollectionId)

const keyOf = (filter: JsonObject): string =>
  typeof filter.field === 'string' ? `field:${filter.field}` : typeof filter.item === 'string' ? `item:${filter.item}` : ''

const targetFor = (filter: JsonObject): ListTarget | null => targetOf(filter, schema.value)

/** The page item's fields whose values fit a filter's literal: same type, same target collection. */
function pageValuesFor(target: ListTarget, op: ListOperator): FieldInfo[] {
  if (!('field' in target)) return []
  const kind = literalFor(target, op)
  if (kind === 'none') return []
  return pageFields.value.filter((f) =>
    kind === 'item'
      ? (f.type === 'reference') && f.target === target.field.target
      : f.type === target.field.type && (kind !== 'option' || f.id === target.field.id))
}

function emitWith(index: number, next: JsonObject | null): void {
  const all = [...filters.value]
  if (next) all[index] = next
  else all.splice(index, 1)
  emit('change', all)
}

function add(): void {
  const first = schema.value?.order[0]
  const filter: JsonObject = first
    ? { field: first, op: operatorsFor({ field: schema.value!.fields[first] })[0] ?? 'eq' }
    : { item: 'slug', op: 'eq', value: '' }
  emit('change', [...filters.value, normalised(filter)])
}

/** A filter with a value its operator takes: none for empty and notEmpty, a blank literal otherwise. */
function normalised(filter: JsonObject): JsonObject {
  const target = targetFor(filter)
  if (!target) return filter
  const op = filter.op as ListOperator
  const { value, ...rest } = filter
  const kind = literalFor(target, op)
  if (kind === 'none') return rest
  if (value !== undefined && filterProblem({ ...rest, value }, schema.value) === null) return { ...rest, value }
  switch (kind) {
    case 'number': return { ...rest, value: 0 }
    case 'boolean': return { ...rest, value: true }
    case 'option': return 'field' in target ? { ...rest, value: Object.keys(target.field.options)[0] ?? '' } : { ...rest, value: '' }
    case 'date': return { ...rest, value: new Date().toISOString().slice(0, 10) }
    default: return { ...rest, value: '' }
  }
}

function onTarget(index: number, event: Event): void {
  const [kind, id] = (event.target as HTMLSelectElement).value.split(':')
  const base: JsonObject = kind === 'field' ? { field: id } : { item: id }
  const target = targetOf(base, schema.value)
  emitWith(index, normalised({ ...base, op: target ? operatorsFor(target)[0] ?? 'eq' : 'eq' }))
}

function onOperator(index: number, event: Event): void {
  emitWith(index, normalised({ ...filters.value[index], op: (event.target as HTMLSelectElement).value }))
}

function onValue(index: number, value: Json): void {
  emitWith(index, { ...filters.value[index], value })
}

/** Which kind of value a filter holds: typed, the page item's field, or "this item". */
function valueSource(filter: JsonObject): string {
  const value = filter.value
  if (isObject(value) && value.current === true) return 'current'
  if (isObject(value) && typeof value.field === 'string') return `page:${value.field}`
  return 'literal'
}

function onValueSource(index: number, event: Event): void {
  const choice = (event.target as HTMLSelectElement).value
  const filter = filters.value[index]
  if (choice === 'current') emitWith(index, { ...filter, value: { current: true } })
  else if (choice.startsWith('page:')) emitWith(index, { ...filter, value: { field: choice.slice(5), scope: 'page' } })
  else {
    const rest = { ...filter }
    delete rest.value
    emitWith(index, normalised(rest))
  }
}

function onLiteral(index: number, kind: string, event: Event): void {
  const raw = (event.target as HTMLInputElement | HTMLSelectElement).value
  if (kind === 'number') {
    const number = Number(raw)
    if (raw.trim() !== '' && Number.isFinite(number)) onValue(index, number)
  } else if (kind === 'boolean') onValue(index, raw === 'true')
  else onValue(index, raw)
}

const problemText = (filter: JsonObject): string | null => {
  const problem = filterProblem(filter, schema.value)
  return problem ? t(`editor.list.filterBroken.${problem}`) : null
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
      v-if="filters.length === 0"
      class="help"
    >
      {{ t('editor.list.noFilters') }}
    </p>
    <div
      v-for="(filter, index) in filters"
      :key="index"
      class="editor-list-row"
      :class="{ 'editor-list-row--broken': !!problemText(filter) }"
      role="group"
      :aria-label="t('editor.list.filterN', { n: index + 1 })"
    >
      <label :for="rowId(index, 'target')">{{ t('editor.list.filterTarget') }}</label>
      <select
        :id="rowId(index, 'target')"
        :value="keyOf(filter)"
        @change="onTarget(index, $event)"
      >
        <option
          v-if="!targetFor(filter)"
          :value="keyOf(filter)"
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

      <template v-if="targetFor(filter)">
        <label :for="rowId(index, 'op')">{{ t('editor.list.filterOperator') }}</label>
        <select
          :id="rowId(index, 'op')"
          :value="String(filter.op)"
          @change="onOperator(index, $event)"
        >
          <option
            v-for="op in operatorsFor(targetFor(filter)!)"
            :key="op"
            :value="op"
          >
            {{ t(`editor.list.operators.${op}`) }}
          </option>
        </select>

        <template v-if="literalFor(targetFor(filter)!, filter.op as ListOperator) !== 'none'">
          <template v-if="pageValuesFor(targetFor(filter)!, filter.op as ListOperator).length > 0 || (sourceIsPageCollection && filter.op === 'ne' && filter.item === 'slug')">
            <label :for="rowId(index, 'from')">{{ t('editor.list.valueFrom') }}</label>
            <select
              :id="rowId(index, 'from')"
              :value="valueSource(filter)"
              @change="onValueSource(index, $event)"
            >
              <option value="literal">
                {{ t('editor.list.valueTyped') }}
              </option>
              <option
                v-for="field in pageValuesFor(targetFor(filter)!, filter.op as ListOperator)"
                :key="field.id"
                :value="`page:${field.id}`"
              >
                {{ t('editor.list.valuePageField', { field: field.name }) }}
              </option>
              <option
                v-if="sourceIsPageCollection && filter.op === 'ne' && filter.item === 'slug'"
                value="current"
              >
                {{ t('editor.list.valueCurrent') }}
              </option>
            </select>
          </template>

          <template v-if="valueSource(filter) === 'literal'">
            <label :for="rowId(index, 'value')">{{ t('editor.list.filterValue') }}</label>
            <select
              v-if="literalFor(targetFor(filter)!, filter.op as ListOperator) === 'boolean'"
              :id="rowId(index, 'value')"
              :value="String(filter.value)"
              @change="onLiteral(index, 'boolean', $event)"
            >
              <option value="true">
                {{ t('editor.list.yes') }}
              </option>
              <option value="false">
                {{ t('editor.list.no') }}
              </option>
            </select>
            <select
              v-else-if="literalFor(targetFor(filter)!, filter.op as ListOperator) === 'option'"
              :id="rowId(index, 'value')"
              :value="String(filter.value ?? '')"
              @change="onLiteral(index, 'option', $event)"
            >
              <option
                v-for="(optionLabel, optionId) in ('field' in targetFor(filter)! ? (targetFor(filter) as { field: FieldInfo }).field.options : {})"
                :key="optionId"
                :value="optionId"
              >
                {{ optionLabel }}
              </option>
            </select>
            <input
              v-else
              :id="rowId(index, 'value')"
              :type="literalFor(targetFor(filter)!, filter.op as ListOperator) === 'number' ? 'number' : literalFor(targetFor(filter)!, filter.op as ListOperator) === 'date' ? 'date' : 'text'"
              :value="filter.value === undefined ? '' : String(filter.value)"
              :maxlength="MAX_TEXT_LITERAL"
              @change="onLiteral(index, literalFor(targetFor(filter)!, filter.op as ListOperator), $event)"
            >
          </template>
        </template>
      </template>

      <p
        v-if="problemText(filter)"
        class="field-error"
      >
        {{ problemText(filter) }}
      </p>
      <button
        type="button"
        class="button button--small"
        @click="emitWith(index, null)"
      >
        {{ t('editor.list.removeFilter', { n: index + 1 }) }}
      </button>
    </div>
    <button
      type="button"
      class="button button--small"
      :disabled="filters.length >= MAX_FILTERS || !schema"
      @click="add"
    >
      {{ t('editor.list.addFilter') }}
    </button>
  </fieldset>
</template>
