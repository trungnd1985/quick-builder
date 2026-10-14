<script setup lang="ts">
// What a template's item pages describe themselves as to search engines (017 US7, FR-035;
// research SE-19): a content type from the contract, and for each of its properties a field of
// the item. Only fields the property accepts are offered; an unbound property shows the default
// it takes; required ones are marked; a binding the site would leave out is marked broken.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { setSeo } from '../ops'
import { useEditorStore } from '../state/editorStore'
import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'
import { choicesFor, STRUCTURED_DATA_TYPES, structuredBroken, type StructuredDataProperty } from './structuredDataTypes'

const props = defineProps<{ id: string; disabled: boolean }>()
const { t, te } = useI18n()
const store = useEditorStore()

const mapping = computed<JsonObject | null>(() => {
  const seo = store.root?.props.seo
  return isObject(seo) && isObject(seo.structuredData) ? seo.structuredData : null
})
const typeName = computed(() => (typeof mapping.value?.type === 'string' ? mapping.value.type : ''))
const type = computed(() => STRUCTURED_DATA_TYPES[typeName.value] ?? null)
const bind = computed<JsonObject>(() => (mapping.value && isObject(mapping.value.bind) ? mapping.value.bind : {}))
const schema = computed(() => {
  const id = store.bindings.templateCollectionId
  return id ? store.bindings.schemas[id] ?? null : null
})
const broken = computed(() => structuredBroken(typeName.value || null, bind.value, schema.value, store.bindings.schemas))

function apply(field: string, value: Json | undefined): void {
  if (store.root) store.apply(setSeo(store.root, field, value, store.selection))
}

function chooseType(name: string): void {
  apply('structuredData', name ? { type: name } : undefined)
}

/** The stored reader of a property, as the key its choice has. */
function chosenKey(property: StructuredDataProperty): string {
  const binding = bind.value[property.name]
  const value = isObject(binding) && isObject(binding.value) ? binding.value : null
  if (!value) return ''
  if (typeof value.field === 'string') return `field:${value.field}`
  if (typeof value.item === 'string') return `item:${value.item}`
  return 'other'
}

function choose(property: StructuredDataProperty, key: string): void {
  const choice = schema.value ? choicesFor(property, schema.value).find((c) => c.key === key) : undefined
  apply(`structuredData.bind.${property.name}`, choice ? { value: choice.value } : undefined)
}

function choiceLabel(label: string): string {
  return te(`editor.binding.builtIn.${label}`) ? t(`editor.binding.builtIn.${label}`) : label
}

function defaultText(property: StructuredDataProperty): string {
  return property.default ? t(`editor.seo.structuredData.defaults.${property.default}`) : t('editor.seo.structuredData.leftOut')
}

function brokenText(reason: string): string {
  return te(`editor.binding.broken.${reason}`) ? t(`editor.binding.broken.${reason}`) : t('editor.seo.structuredData.unknownProperty')
}
</script>

<template>
  <fieldset class="structured-data-control">
    <legend>{{ t('editor.seo.structuredData.label') }}</legend>
    <p class="hint">
      {{ t('editor.seo.structuredData.help') }}
    </p>
    <div class="field">
      <label :for="`${props.id}-type`">{{ t('editor.seo.structuredData.type') }}</label>
      <select
        :id="`${props.id}-type`"
        :value="typeName"
        :disabled="disabled"
        @change="chooseType(($event.target as HTMLSelectElement).value)"
      >
        <option value="">
          {{ t('editor.seo.structuredData.none') }}
        </option>
        <option
          v-for="name in Object.keys(STRUCTURED_DATA_TYPES)"
          :key="name"
          :value="name"
        >
          {{ name }}
        </option>
      </select>
    </div>

    <template v-if="type && schema">
      <div
        v-for="property in type.properties"
        :key="property.name"
        class="field"
      >
        <label :for="`${props.id}-${property.name}`">
          {{ property.name }}<span v-if="property.required"> ({{ t('editor.seo.structuredData.required') }})</span>
        </label>
        <select
          :id="`${props.id}-${property.name}`"
          :value="chosenKey(property)"
          :disabled="disabled"
          :aria-describedby="broken[property.name] ? `${props.id}-${property.name}-broken` : undefined"
          @change="choose(property, ($event.target as HTMLSelectElement).value)"
        >
          <option value="">
            {{ t('editor.seo.structuredData.notBound', { fallback: defaultText(property) }) }}
          </option>
          <option
            v-if="chosenKey(property) && !choicesFor(property, schema).some((c) => c.key === chosenKey(property))"
            :value="chosenKey(property)"
          >
            {{ t('editor.binding.unknownField') }}
          </option>
          <option
            v-for="choice in choicesFor(property, schema)"
            :key="choice.key"
            :value="choice.key"
          >
            {{ choiceLabel(choice.label) }}
          </option>
        </select>
        <p
          v-if="broken[property.name]"
          :id="`${props.id}-${property.name}-broken`"
          class="warning"
          role="note"
        >
          {{ brokenText(broken[property.name]!) }}
        </p>
      </div>
    </template>
  </fieldset>
</template>
