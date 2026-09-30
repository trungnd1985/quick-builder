<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { variantUrl, type MediaFile } from '../../api/media'
import AltTextEditor from './AltTextEditor.vue'

// 013 US1 (details, alternative text), with slots US5 (usage) and US6 (replace) fill in.
// A complementary region beside the list; closing it returns focus to the row's Details
// button (the caller keeps the trigger). Names and alternative text are data: text only.

const props = defineProps<{ siteId: string; file: MediaFile; canEdit: boolean }>()
const emit = defineEmits<{ close: []; changed: [file: MediaFile]; reload: [] }>()

const { t, d, n } = useI18n()
const heading = ref<HTMLElement | null>(null)

const isImage = computed(() => props.file.contentType.startsWith('image/'))

onMounted(async () => {
  await nextTick()
  heading.value?.focus()
})

function size(bytes: number): string {
  if (bytes < 1024) return t('media.size.bytes', { n: n(bytes) })
  if (bytes < 1024 * 1024) return t('media.size.kb', { n: n(bytes / 1024, { maximumFractionDigits: 1 }) })
  return t('media.size.mb', { n: n(bytes / (1024 * 1024), { maximumFractionDigits: 1 }) })
}
</script>

<template>
  <aside
    class="card media-details"
    :aria-labelledby="`media-details-${file.id}`"
    @keydown.esc.prevent="emit('close')"
  >
    <header class="media-details__header">
      <h2
        :id="`media-details-${file.id}`"
        ref="heading"
        tabindex="-1"
      >
        {{ file.fileName }}
      </h2>
      <button
        type="button"
        :aria-label="t('media.details.close', { name: file.fileName })"
        @click="emit('close')"
      >
        {{ t('media.details.closeButton') }}
      </button>
    </header>

    <img
      v-if="isImage && file.available"
      class="media-details__preview"
      :src="variantUrl(file, 960)"
      :alt="file.altText ?? ''"
    >

    <dl class="media-details__facts">
      <dt>{{ t('media.table.type') }}</dt>
      <dd>{{ file.contentType }}</dd>
      <dt>{{ t('media.table.size') }}</dt>
      <dd>{{ size(file.sizeBytes) }}</dd>
      <template v-if="file.width && file.height">
        <dt>{{ t('media.details.dimensions') }}</dt>
        <dd>{{ t('media.details.pixels', { width: file.width, height: file.height }) }}</dd>
      </template>
      <dt>{{ t('media.table.uploaded') }}</dt>
      <dd>
        <time :datetime="file.uploadedAtUtc">{{ d(new Date(file.uploadedAtUtc), 'short') }}</time>
        — {{ file.uploadedBy.displayName || t('media.table.unknownUser') }}
      </dd>
      <template v-if="file.replacedAtUtc">
        <dt>{{ t('media.details.replaced') }}</dt>
        <dd>
          <time :datetime="file.replacedAtUtc">{{ d(new Date(file.replacedAtUtc), 'short') }}</time>
          — {{ file.replacedBy?.displayName || t('media.table.unknownUser') }}
        </dd>
      </template>
    </dl>

    <AltTextEditor
      v-if="isImage"
      :site-id="siteId"
      :file="file"
      :can-edit="canEdit"
      @saved="(saved) => emit('changed', saved)"
      @reload="emit('reload')"
    />

    <slot
      name="usage"
      :file="file"
    />
    <slot
      name="replace"
      :file="file"
    />
  </aside>
</template>
