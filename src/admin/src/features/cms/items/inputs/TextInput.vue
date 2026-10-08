<script setup lang="ts">
// A text field: one line or several, with the characters left shown (015 FR-009).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../../api/cms'
import { codePointLength } from '../../../../editor/schema/subsetValidator'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()

const text = computed(() => (typeof props.value === 'string' ? props.value : ''))
const max = computed(() => props.field.maxLength ?? 256)
const left = computed(() => max.value - codePointLength(text.value))

function update(raw: string): void {
  emit('update:value', raw === '' ? undefined : raw)
}
</script>

<template>
  <div class="item-input item-text">
    <textarea
      v-if="field.multiline"
      :id="id"
      :value="text"
      rows="4"
      :aria-describedby="`${describedBy} ${id}-left`"
      :aria-invalid="invalid"
      :aria-required="field.required"
      @input="update(($event.target as HTMLTextAreaElement).value)"
    />
    <input
      v-else
      :id="id"
      type="text"
      :value="text"
      :aria-describedby="`${describedBy} ${id}-left`"
      :aria-invalid="invalid"
      :aria-required="field.required"
      @input="update(($event.target as HTMLInputElement).value)"
    >
    <p
      :id="`${id}-left`"
      class="help"
      :class="{ 'field-error': left < 0 }"
    >
      {{ left >= 0 ? t('cms.items.form.remaining', { count: left }) : t('cms.items.form.tooLongBy', { count: -left }) }}
    </p>
  </div>
</template>
