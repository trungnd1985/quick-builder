<script setup lang="ts">
// 013 US6 (FR-017 – FR-020): replace a file's contents, keeping everything that points to it.
// The member sees where it is used and that live pages change straight away. A file published
// pages show needs a Designer or Administrator; the dialog says so before any upload. Focus
// moves in, stays in, Escape cancels; the caller returns focus to the Replace button.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ACCEPTED_BY_KIND, ApiError, kindOf, mediaApi, type MediaFile } from '../../api/media'

const props = defineProps<{ siteId: string; file: MediaFile }>()
const emit = defineEmits<{ replaced: [file: MediaFile]; cancel: [] }>()

const { t, te } = useI18n()
const dialog = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const progress = ref<number | null>(null)
const error = ref('')

const blocked = computed(() => !props.file.canReplace)
const usage = computed(() => props.file.usage)
const accept = computed(() => ACCEPTED_BY_KIND[kindOf(props.file)])

onMounted(async () => {
  await nextTick()
  dialog.value?.querySelector<HTMLElement>('input:not([disabled]), button')?.focus()
})

async function onChosen(event: Event): Promise<void> {
  const chosen = (event.target as HTMLInputElement).files?.[0]
  if (!chosen) return

  error.value = ''
  progress.value = 0
  try {
    const replaced = await mediaApi.replace(props.siteId, props.file.id, chosen, props.file.contentVersion, (fraction) => {
      progress.value = Math.round(fraction * 100)
    })
    emit('replaced', replaced)
  } catch (caught) {
    const code = caught instanceof ApiError ? caught.errorCode : undefined
    const key = code?.startsWith('media.') ? `media.errors.${code.slice('media.'.length)}` : undefined
    error.value = key && te(key) ? t(key) : t('media.replace.failed')
  } finally {
    progress.value = null
    if (input.value) input.value.value = ''
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && progress.value === null) {
    event.preventDefault()
    emit('cancel')
    return
  }

  if (event.key !== 'Tab' || !dialog.value) return
  const order = Array.from(dialog.value.querySelectorAll<HTMLElement>('input:not([disabled]), button:not([disabled])'))
  if (order.length === 0) return

  const index = order.indexOf(document.activeElement as HTMLElement)
  const next = event.shiftKey ? order[(index - 1 + order.length) % order.length] : order[(index + 1) % order.length]
  event.preventDefault()
  next?.focus()
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="progress === null && emit('cancel')"
  >
    <div
      ref="dialog"
      class="card dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="replace-media-title"
      aria-describedby="replace-media-body"
      @keydown="onKeydown"
    >
      <h2 id="replace-media-title">
        {{ t('media.replace.title', { name: file.fileName }) }}
      </h2>

      <p id="replace-media-body">
        <template v-if="blocked">
          {{ t('media.errors.in_live_use') }}
        </template>
        <template v-else-if="usage.places > 0">
          {{ t('media.replace.used', { places: usage.places }, usage.places) }}
          <template v-if="usage.inLiveUse">
            {{ t('media.replace.live') }}
          </template>
        </template>
        <template v-else>
          {{ t('media.replace.unused') }}
        </template>
      </p>
      <p class="help">
        {{ t('media.replace.keeps') }}
      </p>

      <div
        v-if="!blocked"
        class="field"
      >
        <label for="replace-media-input">{{ t('media.replace.choose') }}</label>
        <input
          id="replace-media-input"
          ref="input"
          type="file"
          :accept="accept"
          :disabled="progress !== null"
          @change="onChosen"
        >
      </div>

      <p
        v-if="progress !== null"
        role="status"
      >
        {{ t('media.replace.progress', { percent: progress }) }}
      </p>
      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>

      <div class="actions">
        <button
          type="button"
          :disabled="progress !== null"
          @click="emit('cancel')"
        >
          {{ t('media.replace.cancel') }}
        </button>
      </div>
    </div>
  </div>
</template>
