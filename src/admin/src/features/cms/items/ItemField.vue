<script setup lang="ts">
// One field of the item form (015 FR-009, FR-010): its label, required mark, help and problem,
// and the input for its type. The input is described by the help and the problem, and marked
// invalid while it has one, so assistive technology announces both.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../api/cms'
import type { Problem } from '../../../api/items'
import { problemMessage } from './itemErrors'
import BooleanSwitch from './inputs/BooleanSwitch.vue'
import DateInput from './inputs/DateInput.vue'
import MediaInput from './inputs/MediaInput.vue'
import NumberInput from './inputs/NumberInput.vue'
import OptionSelect from './inputs/OptionSelect.vue'
import RichTextInput from './inputs/RichTextInput.vue'
import TextInput from './inputs/TextInput.vue'
import MultiReferencePicker from './MultiReferencePicker.vue'
import ReferencePicker from './ReferencePicker.vue'

const props = defineProps<{
  field: SchemaField
  value: unknown
  siteId: string
  /** The item being edited; null while creating. */
  itemId: string | null
  problem: Problem | null
  /** The schema changed under the form, and this field with it (spec Edge Cases). */
  changed?: boolean
}>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t, te } = useI18n()

const id = computed(() => `item-field-${props.field.id}`)
const helpId = computed(() => `${id.value}-help`)
const errorId = computed(() => `${id.value}-error`)
const describedBy = computed(() => [props.field.help ? helpId.value : '', props.problem ? errorId.value : ''].filter(Boolean).join(' '))
const isMedia = computed(() => props.field.type === 'image' || props.field.type === 'file')
const label = computed(() => (props.field.required ? `${props.field.name} (${t('cms.items.form.required')})` : props.field.name))

const update = (value: unknown): void => emit('update:value', value)
</script>

<template>
  <div
    class="item-field"
    :class="{ 'item-field--invalid': problem, 'item-field--changed': changed }"
    :data-field-id="field.id"
  >
    <label
      v-if="!isMedia && field.type !== 'boolean' && field.type !== 'richText' && field.type !== 'multiReference'"
      :for="id"
      class="item-field__label"
    >
      {{ field.name }}
      <abbr
        v-if="field.required"
        :title="t('cms.items.form.required')"
      >*</abbr>
    </label>
    <span
      v-else-if="!isMedia"
      :id="`${id}-label`"
      class="item-field__label"
    >
      {{ field.name }}
      <abbr
        v-if="field.required && field.type !== 'boolean'"
        :title="t('cms.items.form.required')"
      >*</abbr>
    </span>

    <p
      v-if="field.help"
      :id="helpId"
      class="help"
    >
      {{ field.help }}
    </p>
    <p
      v-if="changed"
      class="help item-field__changed"
    >
      {{ t('cms.items.form.fieldChanged') }}
    </p>

    <TextInput
      v-if="field.type === 'text'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      @update:value="update"
    />
    <RichTextInput
      v-else-if="field.type === 'richText'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      :site-id="siteId"
      :label="label"
      @update:value="update"
    />
    <NumberInput
      v-else-if="field.type === 'number'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      @update:value="update"
    />
    <BooleanSwitch
      v-else-if="field.type === 'boolean'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="`${id}-label ${describedBy}`"
      :invalid="!!problem"
      @update:value="update"
    />
    <DateInput
      v-else-if="field.type === 'date'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      @update:value="update"
    />
    <MediaInput
      v-else-if="isMedia"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      :site-id="siteId"
      :label="label"
      @update:value="update"
    />
    <OptionSelect
      v-else-if="field.type === 'option'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      @update:value="update"
    />
    <ReferencePicker
      v-else-if="field.type === 'reference'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      :site-id="siteId"
      :item-id="itemId"
      @update:value="update"
    />
    <MultiReferencePicker
      v-else-if="field.type === 'multiReference'"
      :id="id"
      :field="field"
      :value="value"
      :described-by="describedBy"
      :invalid="!!problem"
      :site-id="siteId"
      :item-id="itemId"
      :label="field.name"
      @update:value="update"
    />

    <p
      v-if="problem"
      :id="errorId"
      class="field-error"
    >
      {{ problemMessage(t, te, problem) }}
    </p>
  </div>
</template>
