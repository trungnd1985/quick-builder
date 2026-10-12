<script setup lang="ts">
// The item a template page is previewed with (016 FR-020, research TB-14). Its draft values
// fill the bound properties on the canvas, marked as a draft, so a template can be designed
// before anything is published. The choice is a view preference kept in this browser, per
// page; it never changes the page's draft.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ItemRef } from '../api/items'
import ReferencePickerDialog from '../features/cms/items/ReferencePickerDialog.vue'
import { useEditorStore } from './state/editorStore'

const { t } = useI18n()
const store = useEditorStore()

const open = ref(false)
const trigger = ref<HTMLButtonElement | null>(null)
const missing = ref(false)

const sample = computed(() => store.bindings.sampleItem)
const label = computed(() => sample.value?.name ?? sample.value?.slug ?? null)
const noneChosen = new Set<string>()

async function choose(item: ItemRef): Promise<void> {
  close()
  missing.value = !(await store.bindings.chooseSample(item.id))
}

async function clear(): Promise<void> {
  await store.bindings.chooseSample(null)
  missing.value = false
}

function close(): void {
  open.value = false
  trigger.value?.focus()
}
</script>

<template>
  <div
    v-if="store.bindings.templateCollectionId"
    class="sample-item"
    role="group"
    :aria-label="t('editor.sample.label')"
  >
    <span class="help">{{ t('editor.sample.label') }}:</span>
    <span v-if="label">
      {{ label }}
      <span class="badge">{{ t('editor.sample.draft') }}</span>
    </span>
    <span
      v-else
      class="help"
    >{{ missing ? t('editor.sample.missing') : t('editor.sample.none') }}</span>
    <button
      ref="trigger"
      type="button"
      aria-haspopup="dialog"
      @click="open = true"
    >
      {{ t('editor.sample.choose') }}
    </button>
    <button
      v-if="label"
      type="button"
      class="link"
      @click="clear"
    >
      {{ t('editor.sample.clear') }}
    </button>

    <ReferencePickerDialog
      v-if="open"
      :site-id="store.siteId"
      :collection-id="store.bindings.templateCollectionId"
      :title="t('editor.sample.dialogTitle')"
      :disabled="noneChosen"
      :self-id="null"
      @choose="choose"
      @close="close"
    />
  </div>
</template>
