<script setup lang="ts">
// An Image's file, chosen from the site's whole media library in a dialog: folders, search
// and upload into the folder shown (013 US4, MV-23). It opens in the folder this user picked
// from last on this site. Stored by id; a file that has since gone is marked.
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, mediaApi, type MediaFile, type MediaKind } from '../../../api/media'
import MediaPickerDialog from '../../../features/media/MediaPickerDialog.vue'
import { isObject, type Json } from '../../schema/subsetValidator'

const props = withDefaults(
  defineProps<{
    siteId: string
    value: Json | undefined
    label: string
    prop: string
    id: string
    error?: string
    /** What the picker shows first: images for an Image block, or a file field's accepted kind (015). */
    kind?: MediaKind | ''
  }>(),
  { error: undefined, kind: 'image' },
)
const emit = defineEmits<{
  change: [value: Json | undefined]
  /** A file was chosen; its library alternative text, if any, for a block without its own (FR-009). */
  choose: [value: Json, altText: string | null]
}>()
const { t } = useI18n()

const open = ref(false)
const missing = ref(false)
const fileName = ref<string | null>(null)
const isImage = ref(true)
const trigger = ref<HTMLButtonElement | null>(null)

const mediaId = computed(() => (isObject(props.value) && typeof props.value.mediaId === 'string' ? props.value.mediaId : null))

// Marked when the chosen file is gone, or its bytes are.
watch(mediaId, async (id) => {
  missing.value = false
  fileName.value = null
  isImage.value = true
  if (!id) return
  try {
    const file = await mediaApi.get(props.siteId, id)
    missing.value = !file.available
    fileName.value = file.fileName
    isImage.value = file.contentType.startsWith('image/')
  } catch (caught) {
    missing.value = caught instanceof ApiError && caught.status === 404
  }
}, { immediate: true })

function show(): void {
  open.value = true
}

async function close(): Promise<void> {
  open.value = false
  await nextTick()
  trigger.value?.focus()
}

function choose(file: MediaFile): void {
  emit('choose', { mediaId: file.id }, file.altText?.trim() ? file.altText : null)
  void close()
}

</script>

<template>
  <div
    class="editor-field editor-media"
    :data-prop="prop"
  >
    <span
      :id="`${id}-label`"
      class="editor-field__label"
    >{{ label }}</span>
    <div class="editor-media__current">
      <img
        v-if="mediaId && !missing && isImage"
        :src="mediaApi.previewUrl(siteId, mediaId)"
        alt=""
        class="editor-media__preview"
      >
      <span v-else-if="mediaId && !missing && fileName">{{ fileName }}</span>
      <span v-else-if="!mediaId">{{ t('editor.panel.media.none') }}</span>
    </div>
    <div class="editor-media__actions">
      <button
        ref="trigger"
        type="button"
        aria-haspopup="dialog"
        :aria-describedby="`${id}-label`"
        @click="show"
      >
        {{ mediaId ? t('editor.panel.media.change') : t('editor.panel.media.choose') }}
      </button>
      <button
        v-if="mediaId"
        type="button"
        class="link"
        @click="emit('change', undefined)"
      >
        {{ t('editor.panel.media.remove') }}
      </button>
    </div>
    <p
      v-if="missing"
      class="field-error"
    >
      {{ t('editor.panel.media.missing') }}
    </p>
    <p
      v-if="error"
      class="field-error"
    >
      {{ error }}
    </p>

    <MediaPickerDialog
      v-if="open"
      :id="id"
      :site-id="siteId"
      :kind="kind"
      @choose="choose"
      @close="close"
    />
  </div>
</template>
