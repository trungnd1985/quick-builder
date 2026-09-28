<script setup lang="ts">
// An Image's file, chosen from the site's media library, where a member who may upload can
// also add one (FR-017). Stored by id; a file that has since gone is marked.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaApi, type MediaFile } from '../../../api/media'
import { isObject, type Json } from '../../schema/subsetValidator'

const props = defineProps<{ siteId: string; value: Json | undefined; label: string; prop: string; id: string; error?: string }>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const open = ref(false)
const files = ref<MediaFile[]>([])
const loading = ref(false)
const failed = ref(false)
const uploading = ref<number | null>(null)
const uploadError = ref('')

const mediaId = computed(() => (isObject(props.value) && typeof props.value.mediaId === 'string' ? props.value.mediaId : null))
const images = computed(() => files.value.filter((f) => f.contentType.startsWith('image/')))
const missing = computed(() => mediaId.value !== null && open.value && !loading.value && !failed.value && !images.value.some((f) => f.id === mediaId.value))

async function load(): Promise<void> {
  loading.value = true
  failed.value = false
  try {
    const collected: MediaFile[] = []
    let cursor: string | null = null
    do {
      const page = await mediaApi.list(props.siteId, cursor)
      collected.push(...page.items)
      cursor = page.nextCursor
    } while (cursor && collected.length < 500)
    files.value = collected
  } catch {
    failed.value = true
  } finally {
    loading.value = false
  }
}

async function toggle(): Promise<void> {
  open.value = !open.value
  if (open.value && files.value.length === 0) await load()
}

function choose(file: MediaFile): void {
  emit('change', { mediaId: file.id })
  open.value = false
}

async function onUpload(event: Event): Promise<void> {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  uploadError.value = ''
  uploading.value = 0
  try {
    const uploaded = await mediaApi.upload(props.siteId, file, (fraction) => (uploading.value = fraction))
    files.value = [uploaded, ...files.value]
    choose(uploaded)
  } catch (error) {
    uploadError.value = error instanceof Error && error.message ? error.message : t('editor.panel.media.uploadFailed')
  } finally {
    uploading.value = null
  }
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
        v-if="mediaId"
        :src="mediaApi.previewUrl(siteId, mediaId)"
        alt=""
        class="editor-media__preview"
      >
      <span v-else>{{ t('editor.panel.media.none') }}</span>
    </div>
    <div class="editor-media__actions">
      <button
        type="button"
        :aria-expanded="open"
        :aria-controls="`${id}-list`"
        @click="toggle"
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

    <div
      v-if="open"
      :id="`${id}-list`"
      class="editor-media__picker"
    >
      <p
        v-if="loading"
        role="status"
      >
        {{ t('editor.panel.media.loading') }}
      </p>
      <p
        v-else-if="failed"
        role="alert"
      >
        {{ t('editor.panel.media.loadFailed') }}
      </p>
      <p v-else-if="images.length === 0">
        {{ t('editor.panel.media.empty') }}
      </p>
      <ul
        v-else
        class="editor-media__grid"
      >
        <li
          v-for="file in images"
          :key="file.id"
        >
          <button
            type="button"
            :aria-pressed="file.id === mediaId"
            @click="choose(file)"
          >
            <img
              :src="mediaApi.previewUrl(siteId, file.id)"
              alt=""
            >
            <span>{{ file.fileName }}</span>
          </button>
        </li>
      </ul>
      <label
        :for="`${id}-upload`"
        class="editor-media__upload"
      >{{ t('editor.panel.media.upload') }}</label>
      <input
        :id="`${id}-upload`"
        type="file"
        accept="image/*"
        :disabled="uploading !== null"
        @change="onUpload"
      >
      <p
        v-if="uploading !== null"
        role="status"
      >
        {{ t('editor.panel.media.uploading', { percent: Math.round(uploading * 100) }) }}
      </p>
      <p
        v-if="uploadError"
        class="field-error"
        role="alert"
      >
        {{ uploadError }}
      </p>
    </div>
  </div>
</template>
