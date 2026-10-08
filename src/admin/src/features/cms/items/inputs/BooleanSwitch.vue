<script setup lang="ts">
// A yes/no field as a switch; no value counts as "no" (014 FR-010).
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../../api/cms'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()
</script>

<template>
  <button
    :id="id"
    type="button"
    role="switch"
    class="item-input item-switch"
    :aria-checked="props.value === true"
    :aria-describedby="describedBy"
    :aria-invalid="invalid"
    @click="emit('update:value', props.value !== true)"
  >
    {{ props.value === true ? t('cms.items.form.yes') : t('cms.items.filters.no') }}
  </button>
</template>
