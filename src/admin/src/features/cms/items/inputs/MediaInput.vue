<script setup lang="ts">
// An image or file field: chosen from the site's media library through the editor's picker,
// starting on the kinds the field accepts (015 FR-009). Stored as { mediaId }.
import { computed } from 'vue'
import type { SchemaField } from '../../../../api/cms'
import type { MediaKind } from '../../../../api/media'
import MediaControl from '../../../../editor/panel/controls/MediaControl.vue'
import type { Json } from '../../../../editor/schema/subsetValidator'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean; siteId: string; label: string }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()

const current = computed(() => props.value as Json | undefined)

const kind = computed<MediaKind | ''>(() => {
  if (props.field.type === 'image' || props.field.accept === 'images') return 'image'
  return props.field.accept === 'documents' ? 'document' : ''
})
</script>

<template>
  <MediaControl
    :id="id"
    :site-id="siteId"
    :value="current"
    :label="label"
    :prop="field.id"
    :kind="kind"
    @choose="(chosen) => emit('update:value', chosen)"
    @change="(changed) => emit('update:value', changed)"
  />
</template>
