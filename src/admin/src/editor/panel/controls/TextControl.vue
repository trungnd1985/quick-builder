<script setup lang="ts">
// A text property: one line or several, with a live count against its limit. A refused value
// stays in the field, marked, so it can be corrected; the block keeps its last valid value
// (FR-021). A change made elsewhere — on the canvas, by undo — shows here at once (FR-019).
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import FieldShell from './FieldShell.vue'
import { describedBy, type ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'

const props = defineProps<ControlProps & { multiline?: boolean; help?: string }>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const draft = ref(typeof props.value === 'string' ? props.value : '')
watch(() => props.value, (value) => (draft.value = typeof value === 'string' ? value : ''))

const max = computed(() => (typeof props.schema.maxLength === 'number' ? props.schema.maxLength : null))
const length = computed(() => Array.from(draft.value).length)

function onInput(event: Event): void {
  draft.value = (event.target as HTMLInputElement | HTMLTextAreaElement).value
  // An empty optional property is removed, so its default applies.
  emit('change', draft.value === '' && !props.required ? undefined : draft.value)
}
</script>

<template>
  <FieldShell
    :id="id"
    :prop="prop"
    :label="label"
    :help="help"
    :error="error"
  >
    <textarea
      v-if="multiline"
      :id="id"
      :value="draft"
      rows="4"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy(id, error, help)"
      @input="onInput"
    />
    <input
      v-else
      :id="id"
      type="text"
      :value="draft"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy(id, error, help)"
      @input="onInput"
    >
    <p
      v-if="max !== null"
      class="editor-field__count"
      :class="{ 'editor-field__count--over': length > max }"
    >
      {{ t('editor.panel.count', { length, max }) }}
    </p>
  </FieldShell>
</template>
