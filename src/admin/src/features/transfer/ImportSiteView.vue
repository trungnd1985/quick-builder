<script setup lang="ts">
// 020 US2, contracts/admin-transfer.md §2: import an archive as a new site. Upload, then check
// (nothing changes yet), then options (name, a host name when the archive's are all taken,
// start fresh, accepting warnings), then confirm, then progress, then the report.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { transfersApi } from '../../api/transfers'
import ImportCheckView from './ImportCheckView.vue'
import TransferProgress from './TransferProgress.vue'
import TransferReport from './TransferReport.vue'
import { transferErrorMessage } from './transferErrors'
import { useTransfer } from './useTransfer'

const { t, te } = useI18n()
const { transfer, follow, cancel } = useTransfer()

const file = ref<File | null>(null)
const uploading = ref(false)
const uploaded = ref(0)
const working = ref(false)
const error = ref<string | null>(null)
const name = ref('')
const hostName = ref('')
const startFresh = ref(false)
const acceptWarnings = ref(false)

const result = computed(() => transfer.value?.checkResult ?? null)
const hasWarnings = computed(() => result.value?.problems.some((p) => p.severity === 'warning') ?? false)
const needsHostName = computed(() => (result.value?.hostNamesKept?.length ?? 0) === 0)
const stage = computed(() => {
  const state = transfer.value?.state
  if (!state || state === 'uploading' || state === 'uploaded') return 'upload'
  if (state === 'queued' || state === 'checking') return 'checking'
  if (state === 'awaiting_confirmation') return 'confirm'
  if (state === 'refused') return 'refused'
  if (state === 'succeeded') return 'done'
  return 'running'
})
const canConfirm = computed(() =>
  name.value.trim().length > 0 && (!hasWarnings.value || acceptWarnings.value) && (!needsHostName.value || hostName.value.trim().length > 0),
)

watch(result, (next) => {
  if (next?.archive && !name.value) name.value = t('transfer.import.defaultName', { name: next.archive.source.siteName })
})

function choose(event: Event): void {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

async function upload(): Promise<void> {
  if (!file.value) return
  uploading.value = true
  error.value = null
  try {
    const sent = await transfersApi.upload(file.value, (fraction) => (uploaded.value = Math.round(fraction * 100)))
    const stored = await sent.done
    follow(await transfersApi.check(stored.id, 'new_site'))
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    uploading.value = false
  }
}

async function confirm(): Promise<void> {
  if (!transfer.value || !canConfirm.value) return
  working.value = true
  error.value = null
  try {
    follow(await transfersApi.confirm(transfer.value.id, {
      newSiteName: name.value.trim(),
      newSiteHostName: hostName.value.trim() || undefined,
      startFresh: startFresh.value,
      acceptWarnings: acceptWarnings.value,
      checkFingerprint: transfer.value.checkFingerprint,
    }))
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}
</script>

<template>
  <section aria-labelledby="import-heading">
    <header class="page-header">
      <div>
        <h1 id="import-heading">
          {{ t('transfer.import.heading') }}
        </h1>
        <p class="subtitle">
          {{ t('transfer.import.subtitle') }}
        </p>
      </div>
    </header>

    <div
      v-if="stage === 'upload'"
      class="card"
    >
      <label for="import-file">{{ t('transfer.import.file') }}</label>
      <input
        id="import-file"
        type="file"
        accept=".qbsite,application/zip"
        :disabled="uploading"
        @change="choose"
      >
      <div class="actions">
        <button
          type="button"
          class="primary"
          :disabled="!file || uploading"
          @click="upload"
        >
          {{ uploading ? t('transfer.import.uploading', { percent: uploaded }) : t('transfer.import.upload') }}
        </button>
      </div>
    </div>

    <TransferProgress
      v-if="transfer && (stage === 'checking' || stage === 'running')"
      :transfer="transfer"
      @cancel="cancel"
    />

    <div
      v-if="result && (stage === 'confirm' || stage === 'refused')"
      class="card"
    >
      <h2>{{ t('transfer.import.checked') }}</h2>
      <ImportCheckView
        :result="result"
        mode="new_site"
      />

      <p
        v-if="stage === 'refused'"
        class="error"
        role="alert"
      >
        {{ t('transfer.import.refused') }}
      </p>

      <form
        v-else
        class="import-options"
        @submit.prevent="confirm"
      >
        <label for="import-name">{{ t('transfer.import.name') }}</label>
        <input
          id="import-name"
          v-model="name"
          type="text"
          maxlength="200"
          required
        >
        <template v-if="needsHostName">
          <label for="import-host">{{ t('transfer.import.hostName') }}</label>
          <input
            id="import-host"
            v-model="hostName"
            type="text"
            required
            aria-describedby="import-host-help"
          >
          <p
            id="import-host-help"
            class="hint"
          >
            {{ t('transfer.import.hostNameHelp') }}
          </p>
        </template>
        <label>
          <input
            v-model="startFresh"
            type="checkbox"
          >
          {{ t('transfer.import.startFresh') }}
        </label>
        <label v-if="hasWarnings">
          <input
            v-model="acceptWarnings"
            type="checkbox"
          >
          {{ t('transfer.import.acceptWarnings') }}
        </label>
        <div class="actions">
          <button
            type="submit"
            class="primary"
            :disabled="!canConfirm || working"
          >
            {{ t('transfer.import.confirm') }}
          </button>
        </div>
      </form>
    </div>

    <TransferReport
      v-if="stage === 'done' && transfer?.report"
      class="card"
      :report="transfer.report"
    />

    <p
      v-if="transfer && (transfer.state === 'failed' || transfer.state === 'cancelled')"
      class="error"
      role="alert"
    >
      {{ t(`transfer.states.${transfer.state}`) }}
    </p>
    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
  </section>
</template>

<style scoped>
.import-options {
  display: grid;
  gap: 0.5rem;
  max-width: 32rem;
}
</style>
