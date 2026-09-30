<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ACCEPTED_BY_KIND, ACCEPTED_EXTENSIONS, ApiError, mediaApi, type MediaFile, type MediaKind } from '../../api/media'

// 003 US1: upload by choosing files or dropping them, with progress and a result for each
// file (FR-010, FR-027). Every file is its own request, so one refusal never blocks the
// others (research M-10). The privacy notice sits above the controls, before anything is
// sent: files are public at once, embedded details included (clarifications Q1, Q3).

const props = defineProps<{ siteId: string; /** Uploads land in this folder (013 FR-010); null is the top level. */ folderId?: string | null; /** Limits the picker to one kind of file. */ accept?: MediaKind }>()
const emit = defineEmits<{ uploaded: [file: MediaFile] }>()

const { t, te } = useI18n()

/** At most this many uploads in flight; the rest wait their turn. */
const CONCURRENCY = 3

type Status = 'queued' | 'uploading' | 'done' | 'failed'

interface Item {
  key: number
  file: File
  status: Status
  percent: number
  reason: string
}

const items = ref<Item[]>([])
const dragging = ref(false)
const input = ref<HTMLInputElement | null>(null)
let nextKey = 0
let active = 0

function choose(): void {
  input.value?.click()
}

function onChosen(event: Event): void {
  const target = event.target as HTMLInputElement
  enqueue(Array.from(target.files ?? []))
  // Allow choosing the same file again later.
  target.value = ''
}

function onDrop(event: DragEvent): void {
  dragging.value = false
  enqueue(Array.from(event.dataTransfer?.files ?? []))
}

function enqueue(files: File[]): void {
  for (const file of files) {
    items.value.push({ key: nextKey++, file, status: 'queued', percent: 0, reason: '' })
  }

  pump()
}

function pump(): void {
  while (active < CONCURRENCY) {
    const item = items.value.find((i) => i.status === 'queued')
    if (!item) {
      return
    }

    active++
    item.status = 'uploading'
    void send(item).finally(() => {
      active--
      pump()
    })
  }
}

async function send(item: Item): Promise<void> {
  try {
    const stored = await mediaApi.upload(
      props.siteId,
      item.file,
      (fraction) => {
        item.percent = Math.round(fraction * 100)
      },
      props.folderId ?? null,
    )
    item.status = 'done'
    emit('uploaded', stored)
  } catch (caught) {
    item.status = 'failed'
    item.reason = reasonFor(caught)
  }
}

function reasonFor(caught: unknown): string {
  // Error codes are "media.<reason>"; the lookup keys them by <reason>. An unexpected code
  // falls back to a generic message rather than showing a raw key.
  const code = caught instanceof ApiError ? caught.errorCode : undefined
  const key = code?.startsWith('media.') ? `media.errors.${code.slice('media.'.length)}` : undefined
  return key && te(key) ? t(key) : t('media.errors.unknown')
}

function describe(item: Item): string {
  const name = item.file.name
  switch (item.status) {
    case 'queued':
      return t('media.upload.queued', { name })
    case 'uploading':
      return t('media.upload.uploading', { name, percent: item.percent })
    case 'done':
      return t('media.upload.done', { name })
    default:
      return t('media.upload.failed', { name, reason: item.reason })
  }
}
</script>

<template>
  <section
    class="card upload-area"
    aria-labelledby="media-upload-heading"
  >
    <h2 id="media-upload-heading">
      {{ t('media.upload.heading') }}
    </h2>

    <p
      id="media-upload-notice"
      class="notice"
    >
      {{ t('media.upload.notice') }}
    </p>

    <div
      class="drop-zone"
      :class="{ dragging }"
      @dragover.prevent="dragging = true"
      @dragleave="dragging = false"
      @drop.prevent="onDrop"
    >
      <button
        type="button"
        class="primary"
        aria-describedby="media-upload-notice media-upload-types"
        @click="choose"
      >
        {{ t('media.upload.choose') }}
      </button>
      <span class="help">{{ t('media.upload.drop') }}</span>
      <input
        ref="input"
        class="visually-hidden"
        type="file"
        multiple
        tabindex="-1"
        aria-hidden="true"
        :accept="props.accept ? ACCEPTED_BY_KIND[props.accept] : ACCEPTED_EXTENSIONS"
        @change="onChosen"
      >
    </div>
    <p
      id="media-upload-types"
      class="help"
    >
      {{ t('media.upload.types') }}
    </p>

    <!-- Each result is announced as it changes (FR-028). -->
    <ul
      class="upload-results"
      aria-live="polite"
      :aria-label="t('media.upload.resultsLabel')"
    >
      <li
        v-for="item in items"
        :key="item.key"
        :class="`upload-${item.status}`"
      >
        <span>{{ describe(item) }}</span>
        <progress
          v-if="item.status === 'uploading'"
          max="100"
          :value="item.percent"
          :aria-label="item.file.name"
        />
      </li>
    </ul>
  </section>
</template>
