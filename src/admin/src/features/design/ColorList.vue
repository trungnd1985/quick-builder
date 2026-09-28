<script setup lang="ts">
// The site's colours: a name and a #rrggbb value each, with a swatch. Ids are never shown
// or edited — they are what blocks refer to (design-tokens.md §2).
import { useI18n } from 'vue-i18n'
import type { ColorToken } from '../../editor/tokens/defaultTokens'

defineProps<{ colors: ColorToken[]; readOnly: boolean; max: number }>()
const emit = defineEmits<{ add: []; remove: [id: string] }>()
const { t } = useI18n()
</script>

<template>
  <fieldset class="design-group">
    <legend>{{ t('design.colors.title') }}</legend>
    <p class="help">
      {{ t('design.colors.help') }}
    </p>
    <ul class="design-list">
      <li
        v-for="(color, index) in colors"
        :key="color.id"
        class="design-row"
      >
        <span
          class="design-swatch"
          :style="{ background: /^#[0-9a-f]{6}([0-9a-f]{2})?$/.test(color.value) ? color.value : 'transparent' }"
          aria-hidden="true"
        />
        <label
          class="visually-hidden"
          :for="`color-name-${index}`"
        >{{ t('design.colors.name', { position: index + 1 }) }}</label>
        <input
          :id="`color-name-${index}`"
          v-model="color.name"
          type="text"
          maxlength="40"
          :disabled="readOnly"
        >
        <label
          class="visually-hidden"
          :for="`color-value-${index}`"
        >{{ t('design.colors.value', { position: index + 1 }) }}</label>
        <input
          :id="`color-value-${index}`"
          v-model.trim="color.value"
          type="text"
          class="design-hex"
          maxlength="9"
          spellcheck="false"
          :disabled="readOnly"
        >
        <button
          v-if="!readOnly"
          type="button"
          class="link"
          :disabled="colors.length <= 1"
          @click="emit('remove', color.id)"
        >
          {{ t('design.remove') }}
        </button>
      </li>
    </ul>
    <button
      v-if="!readOnly"
      type="button"
      :disabled="colors.length >= max"
      @click="emit('add')"
    >
      {{ t('design.colors.add') }}
    </button>
  </fieldset>
</template>
