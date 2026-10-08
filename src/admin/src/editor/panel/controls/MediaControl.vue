<script setup lang="ts">
// An Image's file, chosen from the site's whole media library in a dialog: folders, search
// and upload into the folder shown (013 US4, MV-23). It opens in the folder this user picked
// from last on this site. Stored by id; a file that has since gone is marked.
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, mediaApi, type MediaFile, type MediaKind } from '../../../api/media'
import { useAuthStore } from '../../../app/authStore'
import MediaBrowser from '../../../features/media/MediaBrowser.vue'
import * as lastFolder from '../../../features/media/lastFolder'
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
const auth = useAuthStore()

const open = ref(false)
const missing = ref(false)
const fileName = ref<string | null>(null)
const isImage = ref(true)
const initialFolderId = ref<string | null>(null)
const trigger = ref<HTMLButtonElement | null>(null)
const dialog = ref<HTMLElement | null>(null)

const mediaId = computed(() => (isObject(props.value) && typeof props.value.mediaId === 'string' ? props.value.mediaId : null))
const userId = computed(() => auth.user?.id ?? 'anonymous')

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

async function show(): Promise<void> {
  initialFolderId.value = lastFolder.get(userId.value, props.siteId)
  open.value = true
  await nextTick()
  dialog.value?.querySelector<HTMLElement>('input, button, select, [tabindex="0"]')?.focus()
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

function rememberFolder(folderId: string | null): void {
  lastFolder.set(userId.value, props.siteId, folderId)
}

/** Keeps keyboard focus inside the dialog, and Escape closes it. */
function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    void close()
    return
  }

  if (event.key !== 'Tab' || !dialog.value) return
  const focusable = Array.from(dialog.value.querySelectorAll<HTMLElement>(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]',
  )).filter((el) => el.getAttribute('tabindex') !== '-1' && !el.closest('[hidden], [aria-hidden="true"]'))
  const first = focusable[0]
  const last = focusable.at(-1)
  if (!first || !last) return

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
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

    <Teleport
      v-if="open"
      to="body"
    >
      <div
        class="dialog-backdrop"
        @click.self="close"
      >
        <div
          ref="dialog"
          class="card dialog dialog--wide editor-media__dialog"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="`${id}-dialog-title`"
          @keydown="onKeydown"
        >
          <header class="page-header">
            <h2 :id="`${id}-dialog-title`">
              {{ t('editor.panel.media.dialogTitle') }}
            </h2>
            <button
              type="button"
              @click="close"
            >
              {{ t('editor.panel.media.close') }}
            </button>
          </header>
          <MediaBrowser
            :site-id="siteId"
            mode="picker"
            :initial-kind="kind"
            :initial-folder-id="initialFolderId"
            @folder-change="rememberFolder"
            @choose="choose"
          />
        </div>
      </div>
    </Teleport>
  </div>
</template>
