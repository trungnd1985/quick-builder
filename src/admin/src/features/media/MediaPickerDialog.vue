<script setup lang="ts">
// The site's whole media library in a dialog, to choose one file: folders, search and upload
// into the folder shown (013 US4, MV-23). It opens in the folder this user picked from last on
// this site. Used by an Image's media control and by rich text's image button (035 FR-010).
//
// The dialog is teleported to the page's body. It carries `data-qb-dialog`, so an editor that
// stops when focus leaves it (a Rich Text block on the canvas) can tell a dialog of its own.
import { nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { MediaFile, MediaKind } from '../../api/media'
import { useAuthStore } from '../../app/authStore'
import MediaBrowser from './MediaBrowser.vue'
import * as lastFolder from './lastFolder'

const props = withDefaults(defineProps<{
  siteId: string
  /** What the browser shows first. */
  kind?: MediaKind | ''
  /** For the dialog's title and its id. */
  id: string
}>(), { kind: 'image' })

const emit = defineEmits<{ choose: [file: MediaFile]; close: [] }>()
const { t } = useI18n()
const auth = useAuthStore()

const dialog = ref<HTMLElement | null>(null)
const userId = auth.user?.id ?? 'anonymous'
const initialFolderId = lastFolder.get(userId, props.siteId)

onMounted(async () => {
  await nextTick()
  dialog.value?.querySelector<HTMLElement>('input, button, select, [tabindex="0"]')?.focus()
})

function rememberFolder(folderId: string | null): void {
  lastFolder.set(userId, props.siteId, folderId)
}

/** Keeps keyboard focus inside the dialog, and Escape closes it. */
function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
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
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      data-qb-dialog
      @click.self="emit('close')"
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
            @click="emit('close')"
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
          @choose="emit('choose', $event)"
        />
      </div>
    </div>
  </Teleport>
</template>
