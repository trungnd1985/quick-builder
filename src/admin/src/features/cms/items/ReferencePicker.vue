<script setup lang="ts">
// A reference field: one item of the target collection, chosen in the picker, or none (015 US4,
// FR-016, FR-017). Choosing replaces any earlier choice.
import { computed, inject, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../api/cms'
import type { ItemRef } from '../../../api/items'
import ReferenceChip from './ReferenceChip.vue'
import ReferencePickerDialog from './ReferencePickerDialog.vue'
import { useItemRefs } from './itemRefs'
import { ITEM_REFS } from './itemRefsKey'

const props = defineProps<{
  field: SchemaField
  value: unknown
  id: string
  describedBy: string
  invalid: boolean
  siteId: string
  itemId: string | null
}>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()
const refs = inject(ITEM_REFS, () => useItemRefs(() => props.siteId), true)

const open = ref(false)
const trigger = ref<HTMLButtonElement | null>(null)
const chosen = computed(() => (typeof props.value === 'string' ? props.value : null))
const disabled = computed(() => new Set(chosen.value ? [chosen.value.toLowerCase()] : []))

watch(chosen, (id) => {
  if (id) void refs.load([id])
}, { immediate: true })

function choose(item: ItemRef): void {
  refs.remember(item)
  emit('update:value', item.id)
  close()
}

function close(): void {
  open.value = false
  trigger.value?.focus()
}
</script>

<template>
  <div class="item-input reference-picker-field">
    <ReferenceChip
      v-if="chosen"
      :id="chosen"
      :state="refs.stateOf(chosen)"
    />
    <span
      v-else
      class="help"
    >{{ t('cms.items.form.none') }}</span>
    <button
      :id="id"
      ref="trigger"
      type="button"
      aria-haspopup="dialog"
      :aria-describedby="describedBy"
      :aria-invalid="invalid"
      @click="open = true"
    >
      {{ t('cms.items.inputs.pickItem') }}
    </button>
    <button
      v-if="chosen"
      type="button"
      class="link"
      @click="emit('update:value', undefined)"
    >
      {{ t('cms.items.form.clear') }}
    </button>

    <ReferencePickerDialog
      v-if="open && field.target"
      :site-id="siteId"
      :collection-id="field.target"
      :title="field.name"
      :disabled="disabled"
      :self-id="itemId"
      @choose="choose"
      @close="close"
    />
  </div>
</template>
