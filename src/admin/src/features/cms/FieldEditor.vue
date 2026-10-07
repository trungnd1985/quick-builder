<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FieldOption, FieldType, SchemaField } from '../../api/cms'
import { FIELD_TYPES, LIMITS, suggestKey, withType } from './collectionSchema'
import { isSingleLineText } from './collectionSchemaRules'
import { typeChangeRule } from './schemaChanges'
import OptionsEditor from './OptionsEditor.vue'

// 014 US1, US4: one field's settings (FR-007, FR-010). A stored field's type change says what
// it will do to values before anything is saved (contract §7); the server decides again
// with the counts. A field added, or turned required, in this save may take a value for the
// items that hold none (FR-029, FR-031).

const props = defineProps<{
  field: SchemaField
  /** The field as stored, or undefined when it is new in this save. */
  stored: SchemaField | undefined
  isTitle: boolean
  autoKey: boolean
  collections: { id: string; name: string }[]
  collectionId: string
  collectionName: string
  readOnly: boolean
  fillInAllowed: boolean
  fillIn: unknown
}>()

const emit = defineEmits<{
  update: [field: SchemaField]
  keyEdited: []
  remove: []
  makeTitle: []
  addOption: []
  fillIn: [value: unknown]
}>()

const { t } = useI18n()

const domId = computed(() => `field-${props.field.id.replace('~', 't')}-settings`)
const typeRule = computed(() => (props.stored && props.stored.type !== props.field.type ? typeChangeRule(props.stored.type, props.field.type) : null))
const targets = computed(() => [
  { id: props.collectionId, name: t('cms.field.thisCollection', { name: props.collectionName }) },
  ...props.collections.filter((c) => c.id !== props.collectionId),
])

function update(change: Partial<SchemaField>): void {
  emit('update', { ...props.field, ...change })
}

function rename(name: string): void {
  update(props.autoKey ? { name, key: suggestKey(name) } : { name })
}

function retype(type: FieldType): void {
  emit('update', withType(props.field, type))
}

function numberOrUndefined(value: string): number | undefined {
  return value.trim() === '' ? undefined : Number(value)
}

function options(next: FieldOption[]): void {
  update({ options: next })
}

/** A fill-in value in the stored form for the field's type (contract §3). */
function setFillIn(raw: string): void {
  if (raw === '') {
    emit('fillIn', undefined)
    return
  }

  switch (props.field.type) {
    case 'number':
      emit('fillIn', Number(raw))
      break
    case 'boolean':
      emit('fillIn', raw === 'true')
      break
    case 'date':
      emit('fillIn', props.field.withTime ? `${new Date(raw).toISOString().slice(0, 19)}Z` : raw)
      break
    default:
      emit('fillIn', raw)
  }
}
</script>

<template>
  <section
    class="card field-editor"
    :aria-labelledby="`${domId}-heading`"
  >
    <h3 :id="`${domId}-heading`">
      {{ t('cms.field.heading') }}
    </h3>

    <p
      v-if="isTitle"
      class="help"
    >
      {{ t('cms.field.isTitle') }}
    </p>

    <fieldset :disabled="readOnly">
      <div class="field">
        <label :for="`${domId}-name`">{{ t('cms.field.name') }}</label>
        <input
          :id="`${domId}-name`"
          :value="field.name"
          type="text"
          :maxlength="LIMITS.name"
          required
          @input="rename(($event.target as HTMLInputElement).value)"
        >
      </div>

      <div class="field">
        <label :for="`${domId}-key`">{{ t('cms.field.key') }}</label>
        <input
          :id="`${domId}-key`"
          :value="field.key"
          type="text"
          :maxlength="LIMITS.key"
          required
          :aria-describedby="`${domId}-key-help`"
          @input="emit('keyEdited'); update({ key: ($event.target as HTMLInputElement).value })"
        >
        <p
          :id="`${domId}-key-help`"
          class="help"
        >
          {{ t('cms.field.keyHelp') }}
        </p>
      </div>

      <div class="field">
        <label :for="`${domId}-help`">{{ t('cms.field.help') }}</label>
        <input
          :id="`${domId}-help`"
          :value="field.help"
          type="text"
          :maxlength="LIMITS.help"
          @input="update({ help: ($event.target as HTMLInputElement).value })"
        >
      </div>

      <div class="field field--checkbox">
        <input
          :id="`${domId}-required`"
          :checked="field.required"
          type="checkbox"
          :disabled="isTitle"
          @change="update({ required: ($event.target as HTMLInputElement).checked })"
        >
        <label :for="`${domId}-required`">{{ t('cms.field.required') }}</label>
      </div>

      <div class="field">
        <label :for="`${domId}-type`">{{ t('cms.field.type') }}</label>
        <select
          :id="`${domId}-type`"
          :value="field.type"
          :aria-describedby="typeRule ? `${domId}-type-rule` : undefined"
          @change="retype(($event.target as HTMLSelectElement).value as FieldType)"
        >
          <option
            v-for="type in FIELD_TYPES"
            :key="type"
            :value="type"
          >
            {{ t(`cms.types.${type}`) }}
          </option>
        </select>
        <p
          v-if="typeRule && typeRule !== 'free'"
          :id="`${domId}-type-rule`"
          :class="typeRule === 'forbidden' ? 'warning' : 'help'"
        >
          {{ t(`cms.field.typeRule.${typeRule}`) }}
        </p>
      </div>

      <template v-if="field.type === 'text'">
        <div class="field field--checkbox">
          <input
            :id="`${domId}-multiline`"
            :checked="field.multiline"
            type="checkbox"
            :disabled="isTitle"
            @change="update({ multiline: ($event.target as HTMLInputElement).checked })"
          >
          <label :for="`${domId}-multiline`">{{ t('cms.field.multiline') }}</label>
        </div>
        <div class="field">
          <label :for="`${domId}-max-length`">{{ t('cms.field.maxLength') }}</label>
          <input
            :id="`${domId}-max-length`"
            :value="field.maxLength"
            type="number"
            min="1"
            :max="LIMITS.textLength"
            @input="update({ maxLength: Number(($event.target as HTMLInputElement).value) })"
          >
        </div>
      </template>

      <template v-if="field.type === 'number'">
        <div class="field">
          <label :for="`${domId}-decimals`">{{ t('cms.field.decimals') }}</label>
          <input
            :id="`${domId}-decimals`"
            :value="field.decimals"
            type="number"
            min="0"
            :max="LIMITS.decimals"
            :aria-describedby="`${domId}-decimals-help`"
            @input="update({ decimals: Number(($event.target as HTMLInputElement).value) })"
          >
          <p
            :id="`${domId}-decimals-help`"
            class="help"
          >
            {{ t('cms.field.decimalsHelp') }}
          </p>
        </div>
        <div class="field">
          <label :for="`${domId}-min`">{{ t('cms.field.min') }}</label>
          <input
            :id="`${domId}-min`"
            :value="field.min ?? ''"
            type="number"
            @input="update({ min: numberOrUndefined(($event.target as HTMLInputElement).value) })"
          >
        </div>
        <div class="field">
          <label :for="`${domId}-max`">{{ t('cms.field.max') }}</label>
          <input
            :id="`${domId}-max`"
            :value="field.max ?? ''"
            type="number"
            @input="update({ max: numberOrUndefined(($event.target as HTMLInputElement).value) })"
          >
        </div>
      </template>

      <div
        v-if="field.type === 'date'"
        class="field field--checkbox"
      >
        <input
          :id="`${domId}-with-time`"
          :checked="field.withTime"
          type="checkbox"
          @change="update({ withTime: ($event.target as HTMLInputElement).checked })"
        >
        <label :for="`${domId}-with-time`">{{ t('cms.field.withTime') }}</label>
      </div>

      <div
        v-if="field.type === 'file'"
        class="field"
      >
        <label :for="`${domId}-accept`">{{ t('cms.field.accept') }}</label>
        <select
          :id="`${domId}-accept`"
          :value="field.accept"
          @change="update({ accept: ($event.target as HTMLSelectElement).value as SchemaField['accept'] })"
        >
          <option value="any">
            {{ t('cms.field.acceptAny') }}
          </option>
          <option value="documents">
            {{ t('cms.field.acceptDocuments') }}
          </option>
          <option value="images">
            {{ t('cms.field.acceptImages') }}
          </option>
        </select>
      </div>

      <OptionsEditor
        v-if="field.type === 'option'"
        :options="field.options ?? []"
        :read-only="readOnly"
        :field-dom-id="domId"
        @change="options"
        @add="emit('addOption')"
      />

      <div
        v-if="field.type === 'reference' || field.type === 'multiReference'"
        class="field"
      >
        <label :for="`${domId}-target`">{{ t('cms.field.target') }}</label>
        <select
          :id="`${domId}-target`"
          :value="field.target"
          @change="update({ target: ($event.target as HTMLSelectElement).value })"
        >
          <option
            value=""
            disabled
          >
            {{ t('cms.field.targetChoose') }}
          </option>
          <option
            v-for="target in targets"
            :key="target.id"
            :value="target.id"
          >
            {{ target.name }}
          </option>
        </select>
      </div>

      <div
        v-if="fillInAllowed"
        class="field"
      >
        <label :for="`${domId}-fill-in`">{{ t('cms.field.fillIn') }}</label>
        <select
          v-if="field.type === 'boolean' || field.type === 'option'"
          :id="`${domId}-fill-in`"
          :value="fillIn === undefined ? '' : String(fillIn)"
          :aria-describedby="`${domId}-fill-in-help`"
          @change="setFillIn(($event.target as HTMLSelectElement).value)"
        >
          <option value="">
            {{ t('cms.field.fillInNone') }}
          </option>
          <template v-if="field.type === 'boolean'">
            <option value="true">
              {{ t('cms.field.fillInTrue') }}
            </option>
            <option value="false">
              {{ t('cms.field.fillInFalse') }}
            </option>
          </template>
          <template v-else>
            <option
              v-for="option in field.options ?? []"
              :key="option.id"
              :value="option.id"
            >
              {{ option.label }}
            </option>
          </template>
        </select>
        <input
          v-else
          :id="`${domId}-fill-in`"
          :value="fillIn === undefined ? '' : String(fillIn)"
          :type="field.type === 'number' ? 'number' : field.type === 'date' ? (field.withTime ? 'datetime-local' : 'date') : 'text'"
          :aria-describedby="`${domId}-fill-in-help`"
          @change="setFillIn(($event.target as HTMLInputElement).value)"
        >
        <p
          :id="`${domId}-fill-in-help`"
          class="help"
        >
          {{ t('cms.field.fillInHelp') }}
        </p>
      </div>

      <div class="actions">
        <button
          v-if="!isTitle && isSingleLineText(field)"
          type="button"
          @click="emit('makeTitle')"
        >
          {{ t('cms.field.makeTitle') }}
        </button>
        <button
          v-if="!isTitle"
          type="button"
          class="danger"
          @click="emit('remove')"
        >
          {{ t('cms.field.remove') }}
        </button>
      </div>
    </fieldset>
  </section>
</template>
