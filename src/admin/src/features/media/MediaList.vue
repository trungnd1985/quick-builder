<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaApi, publicUrl, type MediaFile } from '../../api/media'

// 003 US1/US3: the library as a table — previews, details, the public address and
// deletion. Names are always rendered as text (contracts/media-api.yaml). Buttons appear
// only where they would work; the server enforces the same rules regardless (FR-024).

const props = defineProps<{
  siteId: string
  files: MediaFile[]
  /** The site's host names; the first builds copyable addresses. */
  hostNames: string[]
}>()

const emit = defineEmits<{ delete: [file: MediaFile, trigger: HTMLElement] }>()

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
        {{ t('media.table.caption') }}
      </caption>
      <thead>
        <tr>
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
          <th scope="col">
            {{ t('media.table.uploadedBy') }}
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
          <td class="preview">
            <span
              v-if="!file.available"
              class="badge"
            >{{ t('media.table.unavailable') }}</span>
            <img
              v-else-if="isImage(file)"
              :src="mediaApi.previewUrl(siteId, file.id)"
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
          <td>{{ file.uploadedBy.displayName || t('media.table.unknownUser') }}</td>
          <td class="row-actions">
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
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
