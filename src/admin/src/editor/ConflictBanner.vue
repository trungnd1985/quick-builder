<script setup lang="ts">
// Why the editor is read-only, and the way back (011 FR-028, FR-029, research ED-15).
//
// After a conflict — or while looking at an old recovery copy — the member's version stays on
// screen so blocks can be copied. "Reload latest" brings in the saved draft after saying that
// anything not copied is lost; copied blocks stay on the clipboard to paste back. There is no
// way to overwrite the other save. A published version (012) is read-only for good.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ruleKey } from './editorErrors'
import type { ReadOnlyState } from './state/editorStore'

const props = defineProps<{ state: ReadOnlyState; reloading: boolean; title?: string }>()
const emit = defineEmits<{ reload: [] }>()
const { t, d } = useI18n()

const canReload = computed(() => props.state.reason === 'conflict' || props.state.reason === 'recovery')
const message = computed(() =>
  props.state.reason === 'version'
    ? t('publishing.version.readOnly', {
      number: props.state.versionNumber ?? 0,
      title: props.title ?? '',
      when: props.state.publishedAtUtc ? d(new Date(props.state.publishedAtUtc), 'short') : '',
    })
    : props.state.reason === 'invalid' && props.state.rule
    ? t('editor.readOnly.invalidRule', { reason: t(ruleKey(props.state.rule)) })
    : t(`editor.readOnly.${props.state.reason}`),
)

function reload(): void {
  if (window.confirm(t('editor.conflict.confirmReload'))) emit('reload')
}
</script>

<template>
  <div
    class="editor-banner"
    role="alert"
  >
    <p>{{ message }}</p>
    <button
      v-if="canReload"
      type="button"
      :disabled="reloading"
      @click="reload"
    >
      {{ reloading ? t('editor.conflict.reloading') : t('editor.conflict.reload') }}
    </button>
  </div>
</template>
