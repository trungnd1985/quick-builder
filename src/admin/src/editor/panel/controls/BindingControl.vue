<script setup lang="ts">
// Binds one block property to an item value (016 US2, FR-014 to FR-019; research TB-14).
//
// Offered only inside a binding scope: a template page, or a list's card. The choices are the
// fields and built-in values that can fill this kind of property (bindingRules.ts), grouped by
// scope, nearest first, with single references opening one step further. A bound property
// shows the field's current name and scope, its format, what happens when it is empty, and
// why it is broken when it is. Everything works from the keyboard: the choices are a native
// select, and every other setting a plain control.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { fits, fitsBuiltIn, kindOf, type FieldInfo, type SchemaInfo } from '../../bindings/bindingRules'
import { scopesAt, type ScopeOption } from '../../bindings/scopes'
import { setProp } from '../../ops'
import type { ContentNode } from '../../schema/schema'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import TextTemplateEditor from './TextTemplateEditor.vue'
import { propBroken } from '../../bindings/brokenBindings'

const props = defineProps<{ node: ContentNode; prop: string; label: string }>()
const { t } = useI18n()
const store = useEditorStore()

const id = computed(() => `bind-${props.node.id}-${props.prop}`)
const kind = computed(() => kindOf(props.node.type, props.prop))

/** The scopes this node can read, nearest first; none outside a template or a card. */
const scopes = computed<ScopeOption[]>(() =>
  store.root ? scopesAt(store.root, props.node.id, store.bindings.templateCollectionId, store.bindings.schemas) : [])

// Load the schemas of the scopes' collections, and of the collections their references target.
watch(scopes, async (next) => {
  for (const scope of next) {
    if (!scope.collectionId) continue
    const schema = await store.bindings.schemaOf(scope.collectionId)
    for (const field of Object.values(schema?.fields ?? {})) {
      if (field.type === 'reference' && field.target) await store.bindings.schemaOf(field.target)
    }
  }
}, { immediate: true })

const binding = computed<JsonObject | null>(() => {
  const bind = props.node.props.bind
  const value = isObject(bind) ? bind[props.prop] : undefined
  return isObject(value) ? value : null
})
const value = computed<JsonObject | null>(() => (binding.value && isObject(binding.value.value) ? binding.value.value : null))
const isTemplate = computed(() => !!value.value && Array.isArray(value.value.parts))

interface Choice {
  key: string
  label: string
  value: JsonObject
}

interface ChoiceGroup {
  label: string
  choices: Choice[]
}

const BUILT_IN_LABELS = ['url', 'slug', 'firstPublishedAt', 'lastPublishedAt'] as const

/** The scope as stored: omitted for the nearest one, named otherwise. */
const scopeValue = (scope: ScopeOption, index: number): JsonObject => (index === 0 ? {} : { scope: scope.name })

function choicesFor(schema: SchemaInfo, scopePart: JsonObject, via: FieldInfo | null, prefix: string): Choice[] {
  const k = kind.value
  if (!k) return []
  const viaPart: JsonObject = via ? { via: via.id } : {}
  const choices: Choice[] = []
  for (const fieldId of schema.order) {
    const field = schema.fields[fieldId]
    if (fits(k, field)) {
      choices.push({ key: JSON.stringify({ ...scopePart, ...viaPart, field: field.id }), label: prefix + field.name, value: { ...scopePart, ...viaPart, field: field.id } })
    }
  }
  for (const builtIn of BUILT_IN_LABELS) {
    if (fitsBuiltIn(k, builtIn)) {
      choices.push({
        key: JSON.stringify({ ...scopePart, ...viaPart, item: builtIn }),
        label: prefix + t(`editor.binding.builtIn.${builtIn}`),
        value: { ...scopePart, ...viaPart, item: builtIn },
      })
    }
  }
  return choices
}

const groups = computed<ChoiceGroup[]>(() =>
  scopes.value.flatMap((scope, index) => {
    const schema = scope.collectionId ? store.bindings.schemas[scope.collectionId] : undefined
    if (!schema) return []
    const scopePart = scopeValue(scope, index)
    const scopeLabel = scope.name === 'page' ? t('editor.binding.pageScope') : t('editor.binding.listScope', { list: scope.label ?? scope.name })
    const result: ChoiceGroup[] = [{ label: scopeLabel, choices: choicesFor(schema, scopePart, null, '') }]
    // One step through each single reference (FR-016).
    for (const fieldId of schema.order) {
      const reference = schema.fields[fieldId]
      const target = reference.type === 'reference' && reference.target ? store.bindings.schemas[reference.target] : undefined
      if (target) {
        const choices = choicesFor(target, scopePart, reference, `${reference.name} → `)
        if (choices.length > 0) result.push({ label: `${scopeLabel} · ${reference.name}`, choices })
      }
    }
    return result.filter((g) => g.choices.length > 0)
  }))

const chosenKey = computed(() => {
  const current = value.value
  if (!current || isTemplate.value) return ''
  const { format: _format, ...rest } = current as Record<string, Json>
  void _format
  return JSON.stringify(rest)
})

/** The field the binding reads, for its name and its format options. */
const boundField = computed<FieldInfo | null>(() => {
  const current = value.value
  if (!current || typeof current.field !== 'string') return null
  const scopeIndex = typeof current.scope === 'string' ? scopes.value.findIndex((s) => s.name === current.scope) : 0
  const scope = scopes.value[scopeIndex]
  let schema = scope?.collectionId ? store.bindings.schemas[scope.collectionId] : undefined
  if (schema && typeof current.via === 'string') {
    const target = schema.fields[current.via]?.target
    schema = target ? store.bindings.schemas[target] : undefined
  }
  return schema?.fields[current.field] ?? null
})

// Why the binding is broken, from the scopes and schemas alone (FR-018).
const broken = computed(() => propBroken(props.node, props.prop, scopes.value, store.bindings.schemas))
const noScope = computed(() => !!binding.value && scopes.value.length === 0)

const errorMessage = ref<string | null>(null)

function write(next: JsonObject | null): void {
  if (!store.root) return
  const bind = isObject(props.node.props.bind) ? { ...props.node.props.bind } : {}
  if (next) bind[props.prop] = next
  else delete bind[props.prop]
  const refused = store.apply(setProp(store.root, props.node.id, 'bind', Object.keys(bind).length > 0 ? bind : undefined))
  errorMessage.value = refused ? t('editor.binding.refused') : null
}

function choose(key: string): void {
  if (!key) {
    write(null)
    return
  }
  const choice = groups.value.flatMap((g) => g.choices).find((c) => c.key === key)
  if (!choice) return
  const empty = binding.value?.empty
  write({ value: choice.value, ...(empty === 'hide' ? { empty: 'hide' } : {}) })
}

function setFormat(format: JsonObject | null): void {
  if (!binding.value || !value.value) return
  const { format: _old, ...rest } = value.value as Record<string, Json>
  void _old
  write({ ...binding.value, value: format ? { ...rest, format } : rest })
}

function setEmpty(empty: 'fallback' | 'hide'): void {
  if (!binding.value) return
  const { empty: _old, ...rest } = binding.value as Record<string, Json>
  void _old
  write(empty === 'hide' ? { ...rest, empty: 'hide' } : rest)
}

function useTemplate(): void {
  write({ value: { parts: [''] as Json[] }, ...(binding.value?.empty === 'hide' ? { empty: 'hide' } : {}) })
}

function setParts(parts: Json[]): void {
  write({ ...(binding.value ?? {}), value: { parts } })
}

const format = computed<JsonObject>(() => (value.value && isObject(value.value.format) ? value.value.format : {}))
const dateStyles = ['medium', 'short', 'long', 'iso']
const decimals = [0, 1, 2, 3, 4, 5, 6]
</script>

<template>
  <div
    v-if="kind && (scopes.length > 0 || binding)"
    class="binding-control"
    role="group"
    :aria-labelledby="`${id}-label`"
  >
    <span
      :id="`${id}-label`"
      class="binding-control__label"
    >{{ t('editor.binding.label', { prop: label }) }}</span>

    <p
      v-if="noScope"
      class="warning"
      role="note"
    >
      {{ t('editor.binding.noScope') }}
    </p>

    <template v-if="!isTemplate">
      <label
        :for="`${id}-field`"
        class="visually-hidden"
      >{{ t('editor.binding.field', { prop: label }) }}</label>
      <select
        :id="`${id}-field`"
        :value="chosenKey"
        :aria-describedby="broken ? `${id}-broken` : undefined"
        @change="choose(($event.target as HTMLSelectElement).value)"
      >
        <option value="">
          {{ t('editor.binding.notBound') }}
        </option>
        <option
          v-if="chosenKey && !groups.some((g) => g.choices.some((c) => c.key === chosenKey))"
          :value="chosenKey"
        >
          {{ t('editor.binding.unknownField') }}
        </option>
        <optgroup
          v-for="group in groups"
          :key="group.label"
          :label="group.label"
        >
          <option
            v-for="choice in group.choices"
            :key="choice.key"
            :value="choice.key"
          >
            {{ choice.label }}
          </option>
        </optgroup>
      </select>
      <button
        v-if="kind === 'text' && !binding"
        type="button"
        class="link"
        @click="useTemplate"
      >
        {{ t('editor.binding.useTemplate') }}
      </button>
    </template>

    <TextTemplateEditor
      v-else
      :id="`${id}-template`"
      :parts="(value?.parts as Json[]) ?? []"
      :groups="groups"
      @change="setParts"
    />

    <p
      v-if="broken"
      :id="`${id}-broken`"
      class="warning"
      role="note"
    >
      {{ t(`editor.binding.broken.${broken}`) }}
    </p>

    <template v-if="binding && boundField">
      <div
        v-if="boundField.type === 'date'"
        class="field"
      >
        <label :for="`${id}-date`">{{ t('editor.binding.dateFormat') }}</label>
        <select
          :id="`${id}-date`"
          :value="typeof format.date === 'string' ? format.date : 'medium'"
          @change="setFormat({ date: ($event.target as HTMLSelectElement).value })"
        >
          <option
            v-for="style in dateStyles"
            :key="style"
            :value="style"
          >
            {{ t(`editor.binding.dateStyles.${style}`) }}
          </option>
        </select>
      </div>
      <div
        v-else-if="boundField.type === 'number'"
        class="field"
      >
        <label :for="`${id}-decimals`">{{ t('editor.binding.decimals') }}</label>
        <select
          :id="`${id}-decimals`"
          :value="isObject(format.number) && typeof format.number.decimals === 'number' ? format.number.decimals : boundField.decimals"
          @change="setFormat({ number: { decimals: Number(($event.target as HTMLSelectElement).value) } })"
        >
          <option
            v-for="places in decimals"
            :key="places"
            :value="places"
          >
            {{ places }}
          </option>
        </select>
      </div>
      <div
        v-else-if="boundField.type === 'boolean'"
        class="field"
      >
        <label :for="`${id}-yes`">{{ t('editor.binding.yes') }}</label>
        <input
          :id="`${id}-yes`"
          type="text"
          maxlength="100"
          :value="typeof format.yes === 'string' ? format.yes : ''"
          @change="setFormat({ ...format, yes: ($event.target as HTMLInputElement).value })"
        >
        <label :for="`${id}-no`">{{ t('editor.binding.no') }}</label>
        <input
          :id="`${id}-no`"
          type="text"
          maxlength="100"
          :value="typeof format.no === 'string' ? format.no : ''"
          @change="setFormat({ ...format, no: ($event.target as HTMLInputElement).value })"
        >
      </div>
    </template>

    <fieldset
      v-if="binding"
      class="binding-control__empty"
    >
      <legend>{{ t('editor.binding.whenEmpty') }}</legend>
      <label>
        <input
          type="radio"
          :name="`${id}-empty`"
          :checked="binding.empty !== 'hide'"
          @change="setEmpty('fallback')"
        >
        {{ t('editor.binding.showOwn') }}
      </label>
      <label>
        <input
          type="radio"
          :name="`${id}-empty`"
          :checked="binding.empty === 'hide'"
          @change="setEmpty('hide')"
        >
        {{ t('editor.binding.hideBlock') }}
      </label>
    </fieldset>

    <button
      v-if="binding"
      type="button"
      class="link"
      @click="write(null)"
    >
      {{ t('editor.binding.unbind') }}
    </button>

    <p
      v-if="errorMessage"
      class="error"
      role="alert"
    >
      {{ errorMessage }}
    </p>
  </div>
</template>
