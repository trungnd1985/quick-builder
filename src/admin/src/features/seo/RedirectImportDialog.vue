<script setup lang="ts">
// Import redirects from a CSV file (017 US2, FR-019, SC-002): source, target, kind, and
// optionally match and note. The report lists each refused line by number and reason, never
// its content.
import { nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { redirectsApi, type ImportResult } from '../../api/redirects'
import { redirectErrorMessage } from './redirectErrors'

const props = defineProps<{ siteId: string }>()
const emit = defineEmits<{ imported: []; close: [] }>()
const { t, te } = useI18n()

const dialogId = 'redirect-import'
const fileInput = ref<HTMLInputElement | null>(null)
const file = ref<File | null>(null)
const working = ref(false)
const error = ref<string | null>(null)
const result = ref<ImportResult | null>(null)

function onFile(event: Event): void {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null
  result.value = null
  error.value = null
}

async function run(): Promise<void> {
  if (!file.value) return
  working.value = true
  error.value = null
  try {
    result.value = await redirectsApi.import(props.siteId, file.value)
    if (result.value.added > 0) emit('imported')
  } catch (caught) {
    error.value = redirectErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}

const reason = (code: string) => (te(`redirects.errors.${code.replace(/^redirects\./, '')}`) ? t(`redirects.errors.${code.replace(/^redirects\./, '')}`) : t('redirects.errors.unknown'))

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
}

onMounted(async () => {
  await nextTick()
  fileInput.value?.focus()
})
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('close')"
  >
    <div
      class="card dialog dialog--wide"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-heading`"
      @keydown="onKeydown"
    >
      <h2 :id="`${dialogId}-heading`">
        {{ t('redirects.import.title') }}
      </h2>
      <p
        :id="`${dialogId}-help`"
        class="hint"
      >
        {{ t('redirects.import.help') }}
      </p>

      <div class="field">
        <label :for="`${dialogId}-file`">{{ t('redirects.import.file') }}</label>
        <input
          :id="`${dialogId}-file`"
          ref="fileInput"
          type="file"
          accept=".csv,text/csv"
          :aria-describedby="`${dialogId}-help`"
          @change="onFile"
        >
      </div>

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>

      <section
        v-if="result"
        :aria-labelledby="`${dialogId}-result`"
      >
        <h3
          :id="`${dialogId}-result`"
          role="status"
        >
          {{ t('redirects.import.summary', { added: result.added, refused: result.refused.length }) }}
        </h3>
        <p
          v-if="result.shortened > 0"
          class="hint"
        >
          {{ t('redirects.import.shortened', { count: result.shortened }) }}
        </p>
        <ul v-if="result.refused.length > 0">
          <li
            v-for="refusal in result.refused"
            :key="refusal.line"
          >
            {{ t('redirects.import.line', { line: refusal.line }) }}: {{ reason(refusal.errorCode) }}
          </li>
        </ul>
      </section>

      <div class="actions">
        <button
          type="button"
          @click="emit('close')"
        >
          {{ t('redirects.import.close') }}
        </button>
        <button
          type="button"
          class="primary"
          :disabled="!file || working"
          @click="run"
        >
          {{ t('redirects.import.run') }}
        </button>
      </div>
    </div>
  </div>
</template>
