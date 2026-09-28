<script setup lang="ts">
// A system font stack, previewed in its own face (research ED-17). Nothing is downloaded:
// each stack names fonts the visitor's device already has.
import { useI18n } from 'vue-i18n'
import { fontFamily, fontStackIds } from '../../editor/tokens/fontStacks'

defineProps<{ id: string; label: string; disabled: boolean }>()
const model = defineModel<string>({ required: true })
const { t } = useI18n()
</script>

<template>
  <div class="design-font">
    <label :for="id">{{ label }}</label>
    <select
      :id="id"
      v-model="model"
      :disabled="disabled"
    >
      <option
        v-for="stack in fontStackIds"
        :key="stack"
        :value="stack"
      >
        {{ t(`design.fonts.${stack}`) }}
      </option>
    </select>
    <p
      class="design-font__sample"
      :style="{ fontFamily: fontFamily(model) }"
    >
      {{ t('design.sampleSentence') }}
    </p>
  </div>
</template>
