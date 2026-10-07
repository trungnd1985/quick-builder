<script setup lang="ts">
// An option field: its options in their order, or none (015 FR-009). Stored by option id.
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../../api/cms'

defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()
</script>

<template>
  <select
    :id="id"
    class="item-input item-option"
    :value="typeof value === 'string' ? value : ''"
    :aria-describedby="describedBy"
    :aria-invalid="invalid"
    :aria-required="field.required"
    @change="emit('update:value', ($event.target as HTMLSelectElement).value || undefined)"
  >
    <option value="">
      {{ t('cms.items.inputs.noOption') }}
    </option>
    <option
      v-for="option in field.options ?? []"
      :key="option.id"
      :value="option.id"
    >
      {{ option.label }}
    </option>
  </select>
</template>
