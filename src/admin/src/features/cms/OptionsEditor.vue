<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { FieldOption } from '../../api/cms'

// 014 FR-010: an option field's choices, in order. Items store an option's id, so relabelling
// or reordering changes no item; removing one clears the items that chose it, which the
// preview counts first (FR-025). New options get temporary ids from the builder.

const props = defineProps<{ options: FieldOption[]; readOnly: boolean; fieldDomId: string }>()
const emit = defineEmits<{ change: [options: FieldOption[]]; add: [] }>()
const { t } = useI18n()

function relabel(index: number, label: string): void {
  emit('change', props.options.map((o, i) => (i === index ? { ...o, label } : o)))
}

function remove(index: number): void {
  emit('change', props.options.filter((_, i) => i !== index))
}

function move(index: number, to: number): void {
  if (to < 0 || to >= props.options.length) return
  const next = [...props.options]
  const [option] = next.splice(index, 1)
  if (option) next.splice(to, 0, option)
  emit('change', next)
}
</script>

<template>
  <fieldset class="options-editor">
    <legend>{{ t('cms.options.legend') }}</legend>
    <p
      v-if="options.length === 0"
      class="help"
    >
      {{ t('cms.options.empty') }}
    </p>
    <ol>
      <li
        v-for="(option, index) in options"
        :key="option.id"
      >
        <label
          class="visually-hidden"
          :for="`${fieldDomId}-option-${index}`"
        >{{ t('cms.options.label', { n: index + 1 }) }}</label>
        <input
          :id="`${fieldDomId}-option-${index}`"
          :value="option.label"
          type="text"
          maxlength="100"
          :disabled="readOnly"
          @input="relabel(index, ($event.target as HTMLInputElement).value)"
        >
        <template v-if="!readOnly">
          <button
            type="button"
            :aria-label="t('cms.options.up', { n: index + 1 })"
            :disabled="index === 0"
            @click="move(index, index - 1)"
          >
            {{ t('cms.options.upShort') }}
          </button>
          <button
            type="button"
            :aria-label="t('cms.options.down', { n: index + 1 })"
            :disabled="index === options.length - 1"
            @click="move(index, index + 1)"
          >
            {{ t('cms.options.downShort') }}
          </button>
          <button
            type="button"
            :aria-label="t('cms.options.remove', { n: index + 1 })"
            @click="remove(index)"
          >
            {{ t('cms.options.removeShort') }}
          </button>
        </template>
      </li>
    </ol>
    <button
      v-if="!readOnly"
      type="button"
      @click="emit('add')"
    >
      {{ t('cms.options.add') }}
    </button>
  </fieldset>
</template>

<style scoped>
.options-editor ol {
  list-style: none;
  margin: 0 0 0.5rem;
  padding: 0;
}

.options-editor li {
  display: flex;
  gap: 0.25rem;
  margin-bottom: 0.25rem;
}
</style>
