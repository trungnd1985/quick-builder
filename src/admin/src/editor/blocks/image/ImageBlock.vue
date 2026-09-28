<script setup lang="ts">
// An image from the site's media library, drawn through the admin's authenticated preview
// address — never a public or third-party one. A missing or deleted file shows a placeholder
// and the draft still saves (spec Edge Cases).
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaApi } from '../../../api/media'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { store, prop } = useBlock(() => props.node)

const mediaId = computed(() => {
  const media = props.node.props.media
  return isObject(media) && typeof media.mediaId === 'string' ? media.mediaId : null
})
const failed = ref(false)
watch(mediaId, () => (failed.value = false))

const src = computed(() => (mediaId.value ? mediaApi.previewUrl(store.siteId, mediaId.value) : null))
const alt = computed(() => (prop<boolean>('decorative') ? '' : prop<string | undefined>('alt') ?? ''))
</script>

<template>
  <figure
    class="qb-image"
    :class="`qb-image--${prop('width')}`"
  >
    <img
      v-if="src && !failed"
      :src="src"
      :alt="alt"
      draggable="false"
      @error="failed = true"
    >
    <div
      v-else
      class="qb-editor-placeholder"
    >
      {{ mediaId ? t('editor.blocks.placeholder.imageMissing') : t('editor.blocks.placeholder.image') }}
    </div>
  </figure>
</template>
