<script setup lang="ts">
// Whether a Navigation block collapses behind a menu button, below which of the preview widths,
// and what the button says (024 FR-017, clarification 5). The widths are the editor's mobile and
// tablet previews, so the responsive preview shows exactly where it happens.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import { isObject, type Json } from '../../schema/subsetValidator'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const stored = computed(() => (isObject(props.value) ? props.value : isObject(props.defaultValue) ? props.defaultValue : {}))
const enabled = computed(() => stored.value.enabled === true)
const below = computed(() => (stored.value.below === 'tablet' ? 'tablet' : 'mobile'))
const buttonLabel = ref(typeof stored.value.buttonLabel === 'string' ? stored.value.buttonLabel : '')

watch(stored, (value) => {
  buttonLabel.value = typeof value.buttonLabel === 'string' ? value.buttonLabel : ''
})

function commit(next: { enabled?: boolean; below?: string; buttonLabel?: string }): void {
  const label = (next.buttonLabel ?? buttonLabel.value).trim()
  emit('change', {
    enabled: next.enabled ?? enabled.value,
    below: next.below ?? below.value,
    ...(label ? { buttonLabel: label } : {}),
  })
}
</script>

<template>
  <fieldset
    class="editor-field"
    :data-prop="prop"
  >
    <legend class="editor-field__label">
      {{ label }}
    </legend>
    <label class="checkbox">
      <input
        :id="`${id}-enabled`"
        type="checkbox"
        :checked="enabled"
        @change="commit({ enabled: ($event.target as HTMLInputElement).checked })"
      >
      {{ t('editor.navigation.collapse') }}
    </label>
    <template v-if="enabled">
      <label :for="`${id}-below`">{{ t('editor.navigation.below') }}</label>
      <select
        :id="`${id}-below`"
        :value="below"
        @change="commit({ below: ($event.target as HTMLSelectElement).value })"
      >
        <option value="mobile">
          {{ t('editor.navigation.belowMobile') }}
        </option>
        <option value="tablet">
          {{ t('editor.navigation.belowTablet') }}
        </option>
      </select>
      <label :for="`${id}-button`">{{ t('editor.navigation.buttonLabel') }}</label>
      <input
        :id="`${id}-button`"
        v-model="buttonLabel"
        type="text"
        maxlength="40"
        :placeholder="t('editor.navigation.menuButton')"
        @change="commit({ buttonLabel })"
      >
    </template>
    <p
      v-if="error"
      :id="`${id}-error`"
      class="field-error"
    >
      {{ error }}
    </p>
  </fieldset>
</template>
