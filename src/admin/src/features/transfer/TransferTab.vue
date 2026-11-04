<script setup lang="ts">
// 020 US1, contracts/admin-transfer.md §1: export the site to one archive, follow it, download
// it while it is kept, delete it, and see the site's recent transfers. Administrators only;
// the server checks every action (FR-035).
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Site } from '../../api/sites'
import { ACTIVE_STATES, transfersApi, type ConflictPolicy, type ImportMode, type Transfer } from '../../api/transfers'
import ImportCheckView from './ImportCheckView.vue'
import RestoreConfirmDialog from './RestoreConfirmDialog.vue'
import TransferProgress from './TransferProgress.vue'
import TransferReport from './TransferReport.vue'
import { transferErrorMessage } from './transferErrors'
import { useTransfer } from './useTransfer'

const props = defineProps<{ site: Site }>()

const { t, te, locale } = useI18n()
const { transfer: current, follow, cancel } = useTransfer()

const recent = ref<Transfer[]>([])
const working = ref(false)
const error = ref<string | null>(null)
const status = ref('')

const canTransfer = computed(() => props.site.permissions?.canTransfer === true)
const busy = computed(() => current.value !== null && ACTIVE_STATES.has(current.value.state))
const lastExport = computed(() =>
  current.value?.kind === 'export' && current.value.state === 'succeeded' ? current.value : recent.value.find((x) => x.kind === 'export' && x.downloadable) ?? null,
)

const dateTime = (iso: string | null) =>
  iso ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso)) : ''

const size = (bytes: number | null) => {
  if (bytes === null) return ''
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${new Intl.NumberFormat(locale.value, { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`
}

const summaryCounts = computed(() =>
  Object.entries(lastExport.value?.summary?.counts ?? {})
    .filter(([kind]) => te(`transfer.kinds.${kind}`))
    .map(([kind, count]) => t(`transfer.kinds.${kind}`, { count }, count)),
)

/** A problem code such as `transfer.media_missing`, as the message keyed by its last part. */
function problemText(code: string, id: string): string {
  const key = `transfer.problems.${code.slice(code.lastIndexOf('.') + 1)}`
  return te(key) ? t(key, { id }) : code
}

async function load(): Promise<void> {
  if (!canTransfer.value) return
  try {
    recent.value = await transfersApi.list(props.site.id)
    const active = recent.value.find((x) => ACTIVE_STATES.has(x.state))
    if (active) follow(active)
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  }
}

async function startExport(): Promise<void> {
  working.value = true
  error.value = null
  status.value = ''
  try {
    follow(await transfersApi.startExport(props.site.id))
    status.value = t('transfer.export.started')
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}

async function remove(target: Transfer): Promise<void> {
  if (!window.confirm(t('transfer.export.confirmDelete'))) return
  working.value = true
  error.value = null
  try {
    await transfersApi.deleteArchive(target.id)
    if (current.value?.id === target.id) follow(target.id)
    await load()
    status.value = t('transfer.export.deleted')
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}

// 020 US4: import into this site (merge, with one policy) or restore it from its own archive.
const importFile = ref<File | null>(null)
const importMode = ref<Exclude<ImportMode, 'new_site'>>('merge')
const policy = ref<ConflictPolicy>('skip')
const acceptWarnings = ref(false)
const restoring = ref(false)
const restoreButton = ref<HTMLButtonElement | null>(null)
const POLICIES: ConflictPolicy[] = ['replace', 'skip', 'rename']

const importing = computed(() => current.value?.kind === 'import' && current.value.mode !== 'new_site' ? current.value : null)
const checked = computed(() => (importing.value?.state === 'awaiting_confirmation' || importing.value?.state === 'refused') ? importing.value : null)
const hasWarnings = computed(() => checked.value?.checkResult?.problems.some((p) => p.severity === 'warning') ?? false)

function chooseImport(event: Event, mode: Exclude<ImportMode, 'new_site'>): void {
  importFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
  importMode.value = mode
}

async function checkImport(mode: Exclude<ImportMode, 'new_site'>): Promise<void> {
  if (!importFile.value || importMode.value !== mode) return
  working.value = true
  error.value = null
  status.value = ''
  acceptWarnings.value = false
  try {
    const sent = await transfersApi.upload(importFile.value, () => undefined)
    const stored = await sent.done
    follow(await transfersApi.check(stored.id, mode, props.site.id))
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}

async function confirmMerge(): Promise<void> {
  const target = checked.value
  if (!target || (hasWarnings.value && !acceptWarnings.value)) return
  working.value = true
  error.value = null
  try {
    follow(await transfersApi.confirm(target.id, {
      conflictPolicy: policy.value,
      acceptWarnings: acceptWarnings.value,
      checkFingerprint: target.checkFingerprint,
    }))
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}

async function confirmRestore(typedName: string): Promise<void> {
  const target = checked.value
  if (!target) return
  working.value = true
  error.value = null
  try {
    follow(await transfersApi.confirm(target.id, {
      confirmSiteName: typedName,
      acceptWarnings: true,
      checkFingerprint: target.checkFingerprint,
    }))
    closeRestore()
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  } finally {
    working.value = false
  }
}

function closeRestore(): void {
  restoring.value = false
  restoreButton.value?.focus()
}

async function stop(): Promise<void> {
  try {
    await cancel()
  } catch (caught) {
    error.value = transferErrorMessage(t, te, caught)
  }
}

onMounted(load)
</script>

<template>
  <section
    v-if="canTransfer"
    class="card transfer-tab"
    aria-labelledby="transfer-heading"
  >
    <h2 id="transfer-heading">
      {{ t('transfer.tab.heading') }}
    </h2>
    <p>{{ t('transfer.tab.intro') }}</p>

    <div class="actions">
      <button
        type="button"
        class="primary"
        :disabled="working || busy"
        :aria-describedby="busy ? 'transfer-busy' : undefined"
        @click="startExport"
      >
        {{ t('transfer.export.start') }}
      </button>
      <span
        v-if="busy"
        id="transfer-busy"
        class="hint"
      >{{ t('transfer.export.busy') }}</span>
    </div>

    <TransferProgress
      v-if="current && (busy || current.state === 'failed' || current.state === 'cancelled')"
      :transfer="current"
      :busy="working"
      @cancel="stop"
    />

    <div
      v-if="lastExport"
      class="transfer-tab__export"
    >
      <h3>{{ t('transfer.export.ready') }}</h3>
      <p>
        {{ t('transfer.export.readyBody', { when: dateTime(lastExport.finishedAtUtc), size: size(lastExport.archiveBytes), expires: dateTime(lastExport.archiveExpiresAtUtc) }) }}
      </p>
      <p v-if="summaryCounts.length">
        {{ summaryCounts.join(', ') }}
      </p>
      <div
        v-if="lastExport.summary?.problems.length"
        class="transfer-tab__problems"
      >
        <h4>{{ t('transfer.export.problems', { count: lastExport.summary.problems.length }, lastExport.summary.problems.length) }}</h4>
        <ul>
          <li
            v-for="problem in lastExport.summary.problems"
            :key="`${problem.kind}-${problem.id}`"
          >
            {{ problemText(problem.code, problem.id) }}
          </li>
        </ul>
      </div>
      <div class="actions">
        <a
          class="button-link primary"
          :href="transfersApi.downloadUrl(lastExport.id)"
          download
        >{{ t('transfer.export.download') }}</a>
        <button
          type="button"
          class="danger"
          :disabled="working"
          @click="remove(lastExport)"
        >
          {{ t('transfer.export.delete') }}
        </button>
      </div>
    </div>

    <div class="transfer-tab__import">
      <h3>{{ t('transfer.merge.heading') }}</h3>
      <p>{{ t('transfer.merge.intro') }}</p>
      <label for="merge-file">{{ t('transfer.import.file') }}</label>
      <input
        id="merge-file"
        type="file"
        accept=".qbsite,application/zip"
        :disabled="working || busy"
        @change="chooseImport($event, 'merge')"
      >
      <div class="actions">
        <button
          type="button"
          :disabled="working || busy || !importFile || importMode !== 'merge'"
          @click="checkImport('merge')"
        >
          {{ t('transfer.merge.check') }}
        </button>
      </div>

      <h3>{{ t('transfer.restore.heading') }}</h3>
      <p>{{ t('transfer.restore.intro') }}</p>
      <label for="restore-file">{{ t('transfer.import.file') }}</label>
      <input
        id="restore-file"
        type="file"
        accept=".qbsite,application/zip"
        :disabled="working || busy"
        @change="chooseImport($event, 'restore')"
      >
      <div class="actions">
        <button
          type="button"
          :disabled="working || busy || !importFile || importMode !== 'restore'"
          @click="checkImport('restore')"
        >
          {{ t('transfer.restore.check') }}
        </button>
      </div>
    </div>

    <div
      v-if="checked?.checkResult"
      class="transfer-tab__check"
    >
      <h3>{{ t('transfer.import.checked') }}</h3>
      <ImportCheckView
        :result="checked.checkResult"
        :mode="checked.mode ?? 'merge'"
      />
      <p
        v-if="checked.state === 'refused'"
        class="error"
        role="alert"
      >
        {{ t('transfer.import.refused') }}
      </p>

      <form
        v-else-if="checked.mode === 'merge'"
        class="transfer-tab__options"
        @submit.prevent="confirmMerge"
      >
        <fieldset>
          <legend>{{ t('transfer.merge.policy') }}</legend>
          <label
            v-for="p in POLICIES"
            :key="p"
          >
            <input
              v-model="policy"
              type="radio"
              name="merge-policy"
              :value="p"
            >
            <strong>{{ t(`transfer.merge.policies.${p}.label`) }}</strong>
            {{ t(`transfer.merge.policies.${p}.help`) }}
          </label>
        </fieldset>
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
            :disabled="working || (hasWarnings && !acceptWarnings)"
          >
            {{ t('transfer.merge.confirm') }}
          </button>
        </div>
      </form>

      <div
        v-else-if="checked.mode === 'restore'"
        class="actions"
      >
        <button
          ref="restoreButton"
          type="button"
          class="danger"
          :disabled="working"
          @click="restoring = true"
        >
          {{ t('transfer.restore.open') }}
        </button>
      </div>
    </div>

    <RestoreConfirmDialog
      v-if="restoring && checked?.checkResult"
      :result="checked.checkResult"
      :site-name="site.name"
      :busy="working"
      @confirm="confirmRestore"
      @cancel="closeRestore"
    />

    <TransferReport
      v-if="current?.kind === 'import' && current.state === 'succeeded' && current.report"
      :report="current.report"
    />

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      class="status"
      role="status"
    >
      {{ status }}
    </p>

    <template v-if="recent.length">
      <h3>{{ t('transfer.recent.heading') }}</h3>
      <table class="transfer-tab__recent">
        <thead>
          <tr>
            <th scope="col">
              {{ t('transfer.recent.what') }}
            </th>
            <th scope="col">
              {{ t('transfer.recent.who') }}
            </th>
            <th scope="col">
              {{ t('transfer.recent.when') }}
            </th>
            <th scope="col">
              {{ t('transfer.recent.outcome') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in recent"
            :key="item.id"
          >
            <td>{{ item.kind === 'export' ? t('transfer.recent.export') : t(`transfer.progress.importTitle.${item.mode ?? 'new_site'}`) }}</td>
            <td>{{ item.startedBy?.displayName ?? t('transfer.recent.operator') }}</td>
            <td>{{ dateTime(item.createdAtUtc) }}</td>
            <td>{{ t(`transfer.states.${item.state}`) }}</td>
          </tr>
        </tbody>
      </table>
    </template>
  </section>
</template>

<style scoped>
.transfer-tab {
  display: grid;
  gap: 0.75rem;
}

.transfer-tab__export {
  display: grid;
  gap: 0.5rem;
}

.transfer-tab__recent {
  width: 100%;
  border-collapse: collapse;
}

.transfer-tab__recent th,
.transfer-tab__recent td {
  padding: 0.25rem 0.5rem;
  text-align: start;
}
</style>
