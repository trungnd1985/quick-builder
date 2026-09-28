<script setup lang="ts">
// The frame every property field shares: a label, the control, optional help, and the reason
// a value was refused — tied to the control with aria-describedby (FR-021).
defineProps<{ prop: string; id: string; label: string; help?: string; error?: string; group?: boolean }>()
</script>

<template>
  <div
    class="editor-field"
    :class="{ 'editor-field--invalid': !!error }"
    :data-prop="prop"
  >
    <component
      :is="group ? 'span' : 'label'"
      :id="`${id}-label`"
      class="editor-field__label"
      :for="group ? undefined : id"
    >
      {{ label }}
    </component>
    <slot />
    <p
      v-if="help"
      :id="`${id}-help`"
      class="help"
    >
      {{ help }}
    </p>
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
