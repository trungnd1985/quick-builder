<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, mediaApi, type MediaFile } from '../../api/media'

// 013 FR-008: an image's alternative text, at most 300 characters. The editor copies it into
// Image blocks when the image is chosen (FR-009, MV-04), so changing it here never changes a
// page. A save that loses a race to another change asks the member to reload.

const MAX_LENGTH = 300

const props = defineProps<{ siteId: string; file: MediaFile; canEdit: boolean }>()
const emit = defineEmits<{ saved: [file: MediaFile]; reload: [] }>()

const { t } = useI18n()

const text = ref(props.file.altText ?? '')
const saving = ref(false)
const message = ref('')
const conflict = ref(false)

watch(() => props.file, (file) => {
  text.value = file.altText ?? ''
  message.value = ''
  conflict.value = false
})

const dirty = computed(() => text.value.trim() !== (props.file.altText ?? ''))
const remaining = computed(() => MAX_LENGTH - text.value.length)

async function save(): Promise<void> {
  saving.value = true
  message.value = ''
  conflict.value = false

  try {
    const saved = await mediaApi.updateAltText(props.siteId, props.file.id, text.value.trim() || null)
    message.value = t('media.altText.saved')
    emit('saved', saved)
  } catch (caught) {
    const code = caught instanceof ApiError ? caught.errorCode : undefined
    conflict.value = code === 'media.conflict'
    message.value = code === 'media.conflict'
      ? t('media.altText.conflict')
      : code === 'media.invalid_alt_text'
        ? t('media.altText.tooLong')
        : t('media.altText.failed')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <form
    class="alt-text-editor"
    @submit.prevent="save"
  >
    <label
      class="field"
      :for="`alt-text-${file.id}`"
    >
      {{ t('media.altText.label') }}
    </label>
    <p
      :id="`alt-text-help-${file.id}`"
      class="help"
    >
      {{ t('media.altText.help') }}
    </p>
    <textarea
      :id="`alt-text-${file.id}`"
      v-model="text"
      rows="3"
      :maxlength="MAX_LENGTH"
      :readonly="!canEdit"
      :aria-describedby="`alt-text-help-${file.id} alt-text-count-${file.id}`"
    />
    <p
      :id="`alt-text-count-${file.id}`"
      class="help"
    >
      {{ t('media.altText.remaining', { n: remaining }) }}
    </p>

    <div
      v-if="canEdit"
      class="actions"
    >
      <button
        v-if="conflict"
        type="button"
        @click="emit('reload')"
      >
        {{ t('media.altText.reload') }}
      </button>
      <button
        type="submit"
        class="primary"
        :disabled="saving || !dirty"
      >
        {{ saving ? t('media.altText.saving') : t('media.altText.save') }}
      </button>
    </div>

    <p
      role="status"
      class="help"
    >
      {{ message }}
    </p>
  </form>
</template>
