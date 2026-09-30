<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { publicUrl, type MediaFile } from '../../api/media'

// 003 US1/US3, 013 US1/US4: the library as a table — thumbnails, details, usage, the public
// address, selection for moving, and deletion; in the picker, a Choose action instead.
// Names are always rendered as text (contracts/media-api.yaml). Buttons appear only where
// they would work; the server enforces the same rules regardless.

const props = withDefaults(
  defineProps<{
    siteId: string
    files: MediaFile[]
    /** The site's host names; the first builds copyable addresses. */
    hostNames: string[]
    mode?: 'library' | 'picker'
    /** Library mode: rows ticked for "Move to…". */
    selectedIds?: string[]
    /** Library mode: whether files can be moved (the upload capability). */
    canMove?: boolean
  }>(),
  { mode: 'library', selectedIds: () => [], canMove: false },
)

const emit = defineEmits<{
  delete: [file: MediaFile, trigger: HTMLElement]
  details: [file: MediaFile, trigger: HTMLElement]
  choose: [file: MediaFile]
  toggle: [fileId: string, selected: boolean]
}>()

const { t, d, n } = useI18n()
const root = ref<HTMLElement | null>(null)
const announcement = ref('')

function size(bytes: number): string {
  if (bytes < 1024) {
    return t('media.size.bytes', { n: n(bytes) })
  }

  if (bytes < 1024 * 1024) {
    return t('media.size.kb', { n: n(bytes / 1024, { maximumFractionDigits: 1 }) })
  }

  return t('media.size.mb', { n: n(bytes / (1024 * 1024), { maximumFractionDigits: 1 }) })
}

function isImage(file: MediaFile): boolean {
  return file.contentType.startsWith('image/')
}

/** A 2× source for sharp thumbnails on high-density screens (013 MV-16). */
function thumbnailSrcset(file: MediaFile): string | undefined {
  const [first, second] = file.variants
  return first && second ? `${first.path} 1x, ${second.path} 2x` : undefined
}

async function copy(file: MediaFile): Promise<void> {
  const host = props.hostNames[0]
  if (!host) {
    return
  }

  try {
    await navigator.clipboard.writeText(publicUrl(file, host))
    announcement.value = t('media.copy.done')
  } catch {
    announcement.value = t('media.copy.failed')
  }
}

/** Moves focus to a file's row, for "Load more" (US3): keyboard users land on the first new file. */
async function focusRow(fileId: string): Promise<void> {
  await nextTick()
  root.value?.querySelector<HTMLElement>(`tr[data-file-id="${fileId}"]`)?.focus()
}

defineExpose({ focusRow, announce: (text: string) => (announcement.value = text) })
</script>

<template>
  <div ref="root">
    <p
      class="visually-hidden"
      role="status"
    >
      {{ announcement }}
    </p>

    <table class="sites-table media-table">
      <caption class="visually-hidden">
        {{ mode === 'picker' ? t('media.table.pickerCaption') : t('media.table.caption') }}
      </caption>
      <thead>
        <tr>
          <th
            v-if="mode === 'library' && canMove"
            scope="col"
          >
            <span class="visually-hidden">{{ t('media.table.select') }}</span>
          </th>
          <th scope="col">
            {{ t('media.table.preview') }}
          </th>
          <th scope="col">
            {{ t('media.table.name') }}
          </th>
          <th scope="col">
            {{ t('media.table.type') }}
          </th>
          <th scope="col">
            {{ t('media.table.size') }}
          </th>
          <th scope="col">
            {{ t('media.table.uploaded') }}
          </th>
          <th
            v-if="mode === 'library'"
            scope="col"
          >
            {{ t('media.table.uploadedBy') }}
          </th>
          <th
            v-if="mode === 'library'"
            scope="col"
          >
            {{ t('media.table.usedIn') }}
          </th>
          <th scope="col">
            {{ t('media.table.actions') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="file in files"
          :key="file.id"
          tabindex="-1"
          :data-file-id="file.id"
        >
          <td v-if="mode === 'library' && canMove">
            <input
              type="checkbox"
              :checked="selectedIds.includes(file.id)"
              :aria-label="t('media.table.selectFile', { name: file.fileName })"
              @change="emit('toggle', file.id, ($event.target as HTMLInputElement).checked)"
            >
          </td>
          <td class="preview">
            <span
              v-if="!file.available"
              class="badge"
            >{{ t('media.table.unavailable') }}</span>
            <img
              v-else-if="isImage(file)"
              :src="file.thumbnailPath"
              :srcset="thumbnailSrcset(file)"
              alt=""
              loading="lazy"
            >
            <span
              v-else
              class="badge"
            >{{ t('media.table.pdf') }}</span>
          </td>
          <td class="file-name">
            {{ file.fileName }}
          </td>
          <td>{{ file.contentType }}</td>
          <td>{{ size(file.sizeBytes) }}</td>
          <td>
            <time :datetime="file.uploadedAtUtc">{{ d(new Date(file.uploadedAtUtc), 'short') }}</time>
          </td>
          <td v-if="mode === 'library'">
            {{ file.uploadedBy.displayName || t('media.table.unknownUser') }}
          </td>
          <td v-if="mode === 'library'">
            {{ file.usage.places === 0 ? t('media.table.unused') : t('media.table.places', { n: file.usage.places }, file.usage.places) }}
          </td>
          <td class="row-actions">
            <template v-if="mode === 'picker'">
              <button
                type="button"
                class="primary"
                :aria-label="t('media.picker.chooseFile', { name: file.fileName })"
                @click="emit('choose', file)"
              >
                {{ t('media.picker.choose') }}
              </button>
            </template>
            <template v-else>
              <button
                type="button"
                :aria-label="t('media.details.label', { name: file.fileName })"
                @click="emit('details', file, $event.currentTarget as HTMLElement)"
              >
                {{ t('media.details.button') }}
              </button>
              <button
                v-if="hostNames.length > 0 && file.available"
                type="button"
                :aria-label="t('media.copy.label', { name: file.fileName })"
                @click="copy(file)"
              >
                {{ t('media.copy.button') }}
              </button>
              <button
                v-if="file.canDelete"
                type="button"
                class="danger"
                :aria-label="t('media.delete.label', { name: file.fileName })"
                @click="emit('delete', file, $event.currentTarget as HTMLElement)"
              >
                {{ t('media.delete.button') }}
              </button>
            </template>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
