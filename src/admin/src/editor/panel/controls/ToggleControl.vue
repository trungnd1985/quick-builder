<script setup lang="ts">
// A yes-or-no property.
import { computed } from 'vue'
import { describedBy, type ControlProps } from './types'
import type { Json } from '../../schema/subsetValidator'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()

const checked = computed(() => (props.value ?? props.defaultValue) === true)
</script>

<template>
  <div
    class="editor-field editor-field--toggle"
    :data-prop="prop"
  >
    <label class="checkbox">
      <input
        :id="id"
        type="checkbox"
        :checked="checked"
        :aria-describedby="describedBy(id, error)"
        @change="emit('change', ($event.target as HTMLInputElement).checked)"
      >
      {{ label }}
    </label>
    <p
      v-if="error"
      :id="`${id}-error`"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
  </div>
</template>
