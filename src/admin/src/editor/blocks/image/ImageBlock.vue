<script setup lang="ts">
// An image from the site's media library, drawn through the admin's authenticated addresses
// — never a public or third-party one. Like a published page (013 MV-15, MV-16) it offers the
// file's resized copies and states its size, so the canvas loads small images and does not
// jump as they arrive. A missing or deleted file shows a placeholder and the draft still
// saves (011 spec Edge Cases).
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaApi, type MediaFile } from '../../../api/media'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { useBrokenLinks } from '../brokenLinks'
import { useBlock } from '../useBlock'
import { useBorder } from '../useBorder'
import { defaultSource, mediaDetails, sourceSet } from './mediaDetails'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { store, prop } = useBlock(() => props.node)
const isBroken = useBrokenLinks(() => store.siteId)
const broken = computed(() => isBroken.value(props.node.props.link))

const mediaId = computed(() => {
  const media = props.node.props.media
  return isObject(media) && typeof media.mediaId === 'string' ? media.mediaId : null
})
const failed = ref(false)
const details = ref<MediaFile | null>(null)

watch(
  mediaId,
  async (id) => {
    failed.value = false
    details.value = null
    if (!id) return
    const file = await mediaDetails(store.siteId, id)
    if (mediaId.value === id) details.value = file
  },
  { immediate: true },
)

const preview = computed(() => (mediaId.value ? mediaApi.previewUrl(store.siteId, mediaId.value) : null))
const src = computed(() => (details.value ? defaultSource(details.value) : undefined) ?? preview.value)
const srcset = computed(() => (details.value ? sourceSet(details.value) : undefined))
const width = computed(() => (typeof details.value?.width === 'number' ? details.value.width : undefined))
const height = computed(() => (typeof details.value?.height === 'number' ? details.value.height : undefined))
// 066 BB-05: a border frames the picture itself, so its radius rounds the photo.
const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)
const alt = computed(() => (prop<boolean>('decorative') ? '' : prop<string | undefined>('alt') ?? ''))
</script>

<template>
  <figure
    class="qb-image"
    :class="`qb-image--${prop('width')}`"
  >
    <img
      v-if="src && !failed"
      :class="borderClasses"
      :style="borderStyle"
      :src="src"
      :srcset="srcset"
      :sizes="srcset ? '100vw' : undefined"
      :width="width"
      :height="height"
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
    <span
      v-if="broken"
      class="qb-editor-broken-link"
    >{{ t('editor.canvas.brokenLink') }}</span>
  </figure>
</template>
