<script setup lang="ts">
// A scale of named steps — the site's text sizes or its spacing — each a name and a size in
// multiples of the base text size (design-tokens.md §2).
import { useI18n } from 'vue-i18n'
import type { ScaleToken } from '../../editor/tokens/defaultTokens'

const props = defineProps<{
  steps: ScaleToken[]
  kind: 'sizes' | 'spacing'
  readOnly: boolean
  min: number
  max: number
  maxSteps: number
}>()
const emit = defineEmits<{ add: []; remove: [id: string] }>()
const { t } = useI18n()

const id = (part: string, index: number) => `${props.kind}-${part}-${index}`
</script>

<template>
  <fieldset class="design-group">
    <legend>{{ t(`design.${kind}.title`) }}</legend>
    <p class="help">
      {{ t(`design.${kind}.help`) }}
    </p>
    <ul class="design-list">
      <li
        v-for="(step, index) in steps"
        :key="step.id"
        class="design-row"
      >
        <label
          class="visually-hidden"
          :for="id('name', index)"
        >{{ t('design.stepName', { position: index + 1 }) }}</label>
        <input
          :id="id('name', index)"
          v-model="step.name"
          type="text"
          maxlength="40"
          :disabled="readOnly"
        >
        <label
          class="visually-hidden"
          :for="id('size', index)"
        >{{ t('design.stepSize', { position: index + 1 }) }}</label>
        <input
          :id="id('size', index)"
          v-model.number="step.rem"
          type="number"
          :min="min"
          :max="max"
          step="0.125"
          class="design-size"
          :disabled="readOnly"
        >
        <span
          v-if="kind === 'sizes'"
          class="design-preview"
          :style="{ fontSize: `${step.rem}rem` }"
          aria-hidden="true"
        >{{ t('design.sample') }}</span>
        <span
          v-else
          class="design-preview design-preview--spacing"
          :style="{ width: `${step.rem}rem` }"
          aria-hidden="true"
        />
        <button
          v-if="!readOnly"
          type="button"
          class="link"
          :disabled="steps.length <= 1"
          @click="emit('remove', step.id)"
        >
          {{ t('design.remove') }}
        </button>
      </li>
    </ul>
    <button
      v-if="!readOnly"
      type="button"
      :disabled="steps.length >= maxSteps"
      @click="emit('add')"
    >
      {{ t(`design.${kind}.add`) }}
    </button>
  </fieldset>
</template>
