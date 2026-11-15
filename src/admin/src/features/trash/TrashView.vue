<script setup lang="ts">
// The site's trash (021 FR-009 to FR-012, contracts/admin-trash.md §2): one tab per kind, each
// served by the module that owns it. A tab the member may not view answers forbidden and is
// left out. Entries are newest first, 50 at a time, with "Load more".
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../../api/request'
import { cmsApi } from '../../api/cms'
import { mediaApi } from '../../api/media'
import { purgeMany, restoreMany, trashApi, trashPurgeApi, type BulkOutcome, type TrashEntry, type TrashKind } from '../../api/trash'
import { useSitesStore } from '../../app/sitesStore'
import RestoreDialog from './RestoreDialog.vue'
import EmptyTrashDialog from './EmptyTrashDialog.vue'
import TrashPreview from './TrashPreview.vue'
import { useTrashRetention } from './useTrashRetention'

const { t, d } = useI18n()
const route = useRoute()
const router = useRouter()
const sites = useSitesStore()

const KINDS: TrashKind[] = ['page', 'component', 'item', 'file']

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')
const kind = computed<TrashKind>(() => (KINDS.includes(route.query.tab as TrashKind) ? (route.query.tab as TrashKind) : 'page'))

const hidden = ref<Set<TrashKind>>(new Set())
const entries = ref<TrashEntry[]>([])
const next = ref<string | null>(null)
const emptying = ref(false)
// 021 FR-021: permanent delete and emptying are for Administrators; the list says who may.
const canPurge = computed(() => entries.value.some((e) => e.canPurge))
const total = ref(0)
const totalBytes = ref<number | null>(null)
const search = ref('')
const loading = ref(false)
const error = ref<string | null>(null)
const status = ref('')
const restoring = ref<TrashEntry | null>(null)
const previewing = ref<TrashEntry | null>(null)

// FR-010: who deleted it, when, and (items, media) where it was.
const trashedBy = ref('')
const from = ref('')
const to = ref('')
const place = ref('')
const places = ref<{ id: string; name: string }[]>([])
// Everyone seen deleting something so far; the list has no separate source of names.
const deleters = ref(new Map<string, string>())

// FR-019: restoring several; entries that need a choice are restored on their own.
const selected = ref(new Set<string>())
const outcome = ref<BulkOutcome | null>(null)
const bulkBusy = ref(false)
const restorable = (entry: TrashEntry): boolean => entry.canRestore && entry.place?.parentState !== 'trashed' && entry.place?.parentState !== 'gone'
const selectable = (entry: TrashEntry): boolean => restorable(entry) || entry.canPurge
const chosen = computed(() => entries.value.filter((e) => selected.value.has(e.id)))
const canRestoreSelected = computed(() => chosen.value.length > 0 && chosen.value.every(restorable))
const canPurgeSelected = computed(() => chosen.value.length > 0 && chosen.value.every((e) => e.canPurge))
// The operator's setting (FR-025), which only the single-site read carries.
const retentionDays = useTrashRetention(String(route.params.siteId))

function toggle(entry: TrashEntry): void {
  const next = new Set(selected.value)
  if (next.has(entry.id)) next.delete(entry.id)
  else next.add(entry.id)
  selected.value = next
}

async function purgeSelected(): Promise<void> {
  if (!window.confirm(t('trash.bulk.purgeConfirm', { n: chosen.value.length }))) return
  bulkBusy.value = true
  error.value = null
  try {
    outcome.value = await purgeMany(siteId.value, kind.value, chosen.value.map((e) => e.id))
    selected.value = new Set()
    await load()
  } catch {
    error.value = t('trash.bulk.failed')
  } finally {
    bulkBusy.value = false
  }
}

async function restoreSelected(): Promise<void> {
  bulkBusy.value = true
  error.value = null
  try {
    const plans = await Promise.all(chosen.value.map((e) => trashApi.restorePlan(siteId.value, e.kind, e.id).then((p) => ({ id: e.id, confirmedAddress: p.address }))))
    outcome.value = await restoreMany(siteId.value, kind.value, plans)
    selected.value = new Set()
    await load()
  } catch {
    error.value = t('trash.bulk.failed')
  } finally {
    bulkBusy.value = false
  }
}

let debounce: ReturnType<typeof setTimeout> | undefined

async function load(more = false): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const page = await trashApi.list(siteId.value, kind.value, {
      q: search.value.trim() || undefined,
      trashedBy: trashedBy.value || undefined,
      from: from.value ? new Date(from.value).toISOString() : undefined,
      to: to.value ? new Date(to.value + 'T23:59:59').toISOString() : undefined,
      collectionId: kind.value === 'item' && place.value ? place.value : undefined,
      folderId: kind.value === 'file' && place.value ? place.value : undefined,
      after: more ? next.value : null,
    })
    entries.value = more ? [...entries.value, ...page.entries] : page.entries
    for (const entry of page.entries) deleters.value.set(entry.trashedBy.userId, entry.trashedBy.displayName)
    next.value = page.nextCursor
    total.value = page.total
    totalBytes.value = page.totalBytes ?? null
  } catch (caught) {
    if (caught instanceof ApiError && caught.problem?.errorCode === 'identity.forbidden') {
      hidden.value = new Set([...hidden.value, kind.value])
      const visible = KINDS.find((k) => !hidden.value.has(k))
      if (visible && visible !== kind.value) await selectTab(visible)
      return
    }

    error.value = t('trash.loadFailed')
  } finally {
    loading.value = false
  }
}

async function selectTab(tab: TrashKind): Promise<void> {
  await router.replace({ query: { ...route.query, tab } })
}

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  await Promise.all([load(), loadPlaces()])
})

async function loadPlaces(): Promise<void> {
  place.value = ''
  places.value = []
  try {
    if (kind.value === 'item') places.value = (await cmsApi.list(siteId.value)).collections.map((c) => ({ id: c.id, name: c.name }))
    if (kind.value === 'file') places.value = (await mediaApi.folders.list(siteId.value)).folders.map((f) => ({ id: f.id, name: f.name }))
  } catch {
    places.value = []
  }
}

watch([siteId, kind], () => {
  entries.value = []
  selected.value = new Set()
  outcome.value = null
  void loadPlaces()
  void load()
})

watch([trashedBy, from, to, place], () => void load())

// FR-010: search narrows as the member types, without a request per key.
watch(search, () => {
  clearTimeout(debounce)
  debounce = setTimeout(() => void load(), 300)
})

const when = (iso: string): string => d(new Date(iso), 'short')

function where(entry: TrashEntry): string {
  const place = entry.place
  if (!place) return ''
  if (place.parentState) {
    if (place.parentState === 'none') return t('trash.where.topLevel')
    return t(`trash.where.parent.${place.parentState}`, { parent: place.parentLabel ?? '' })
  }

  if (place.collectionId) return place.collectionName ?? ''
  if (place.folderState) {
    if (place.folderState === 'none') return t('trash.where.library')
    return t(`trash.where.folder.${place.folderState}`, { folder: place.folderName ?? '' })
  }

  return ''
}

function size(bytes: number): string {
  if (bytes < 1024) return t('trash.size.bytes', { n: bytes })
  if (bytes < 1024 * 1024) return t('trash.size.kb', { n: (bytes / 1024).toFixed(1) })
  return t('trash.size.mb', { n: (bytes / (1024 * 1024)).toFixed(1) })
}

/** 021 US5: deletes one entry permanently, after the browser's own confirmation. */
async function purge(entry: TrashEntry): Promise<void> {
  const question = entry.kind === 'file' ? 'trash.purge.confirmFile' : 'trash.purge.confirm'
  if (!window.confirm(t(question, { label: entry.label }))) return
  try {
    await trashPurgeApi.purge(siteId.value, entry.kind, entry.id)
    entries.value = entries.value.filter((e) => e.id !== entry.id)
    total.value = Math.max(0, total.value - 1)
    status.value = t('trash.purge.done', { label: entry.label })
  } catch {
    error.value = t('trash.purge.failed', { label: entry.label })
  }
}

async function emptied(removed: number): Promise<void> {
  emptying.value = false
  status.value = t('trash.emptyTrash.done', { n: removed })
  await load()
}

function restored(published: boolean): void {
  const entry = restoring.value
  restoring.value = null
  if (!entry) return
  entries.value = entries.value.filter((e) => e.id !== entry.id)
  total.value = Math.max(0, total.value - 1)
  status.value = t(published ? 'trash.restoredAndPublished' : 'trash.restored', { label: entry.label })
}
</script>

<template>
  <section aria-labelledby="trash-heading">
    <header class="page-header">
      <div>
        <h1 id="trash-heading">
          {{ t('trash.title') }}
        </h1>
        <p class="subtitle">
          {{ t('trash.intro', { site: siteName }) }}
        </p>
      </div>
      <button
        v-if="canPurge"
        type="button"
        class="danger"
        @click="emptying = true"
      >
        {{ t('trash.emptyTrash.action') }}
      </button>
    </header>

    <div
      role="tablist"
      :aria-label="t('trash.kinds')"
      class="trash-tabs"
    >
      <template
        v-for="tab in KINDS"
        :key="tab"
      >
        <button
          v-if="!hidden.has(tab)"
          :id="`trash-tab-${tab}`"
          type="button"
          role="tab"
          :aria-selected="kind === tab"
          :aria-controls="`trash-panel`"
          @click="selectTab(tab)"
        >
          {{ t(`trash.tab.${tab}`) }}
        </button>
      </template>
    </div>

    <div
      id="trash-panel"
      role="tabpanel"
      :aria-labelledby="`trash-tab-${kind}`"
    >
      <div
        class="trash-toolbar"
        role="search"
      >
        <div class="field trash-toolbar__search">
          <label for="trash-search">{{ t('trash.search') }}</label>
          <input
            id="trash-search"
            v-model="search"
            type="search"
          >
        </div>
        <div class="field">
          <label for="trash-by">{{ t('trash.filter.by') }}</label>
          <select
            id="trash-by"
            v-model="trashedBy"
          >
            <option value="">
              {{ t('trash.filter.anyone') }}
            </option>
            <option
              v-for="[id, name] in deleters"
              :key="id"
              :value="id"
            >
              {{ name }}
            </option>
          </select>
        </div>
        <div class="field">
          <label for="trash-from">{{ t('trash.filter.from') }}</label>
          <input
            id="trash-from"
            v-model="from"
            type="date"
          >
        </div>
        <div class="field">
          <label for="trash-to">{{ t('trash.filter.to') }}</label>
          <input
            id="trash-to"
            v-model="to"
            type="date"
          >
        </div>
        <div
          v-if="kind === 'item' || kind === 'file'"
          class="field"
        >
          <label for="trash-place">{{ t(kind === 'item' ? 'trash.filter.collection' : 'trash.filter.folder') }}</label>
          <select
            id="trash-place"
            v-model="place"
          >
            <option value="">
              {{ t('trash.filter.anywhere') }}
            </option>
            <option
              v-for="option in places"
              :key="option.id"
              :value="option.id"
            >
              {{ option.name }}
            </option>
          </select>
        </div>
      </div>

      <div
        v-if="selected.size > 0"
        class="trash-bulk"
      >
        <button
          v-if="canRestoreSelected"
          type="button"
          :disabled="bulkBusy"
          @click="restoreSelected"
        >
          {{ t('trash.bulk.restore', { n: selected.size }) }}
        </button>
        <button
          v-if="canPurgeSelected"
          type="button"
          class="danger"
          :disabled="bulkBusy"
          @click="purgeSelected"
        >
          {{ t('trash.bulk.purge', { n: selected.size }) }}
        </button>
      </div>
      <div
        v-if="outcome"
        role="status"
      >
        <p>{{ t('trash.bulk.result', { done: outcome.succeeded.length, failed: outcome.failed.length }) }}</p>
        <ul v-if="outcome.failed.length > 0">
          <li
            v-for="failure in outcome.failed"
            :key="failure.id"
          >
            {{ entries.find((e) => e.id === failure.id)?.label ?? failure.id }}: {{ failure.detail }}
          </li>
        </ul>
      </div>

      <p v-if="kind === 'file' && totalBytes !== null">
        {{ t('trash.mediaTotal', { n: total, size: size(totalBytes) }) }}
      </p>

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
        <button
          type="button"
          @click="load()"
        >
          {{ t('trash.retry') }}
        </button>
      </p>
      <p
        class="visually-hidden"
        role="status"
      >
        {{ status }}
      </p>

      <p
        v-if="loading && entries.length === 0"
        aria-busy="true"
      >
        {{ t('trash.loading') }}
      </p>

      <p
        v-else-if="!loading && !error && entries.length === 0"
        class="card"
      >
        {{ t('trash.empty', { days: retentionDays ?? 30 }) }}
      </p>

      <table
        v-if="entries.length > 0"
        class="sites-table"
      >
        <caption class="visually-hidden">
          {{ t(`trash.tab.${kind}`) }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              <span class="visually-hidden">{{ t('trash.columns.select') }}</span>
            </th>
            <th scope="col">
              {{ t('trash.columns.label') }}
            </th>
            <th scope="col">
              {{ t('trash.columns.where') }}
            </th>
            <th scope="col">
              {{ t('trash.columns.deleted') }}
            </th>
            <th scope="col">
              {{ t('trash.columns.purge') }}
            </th>
            <th scope="col">
              <span class="visually-hidden">{{ t('trash.columns.actions') }}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="entry in entries"
            :key="entry.id"
          >
            <td>
              <input
                v-if="selectable(entry)"
                type="checkbox"
                :aria-label="t('trash.selectLabel', { label: entry.label })"
                :checked="selected.has(entry.id)"
                @change="toggle(entry)"
              >
            </td>
            <th scope="row">
              {{ entry.label }}
              <span
                v-if="entry.address && entry.address !== entry.label"
                class="subtitle"
              >{{ entry.address }}</span>
              <span
                v-if="entry.wasLive"
                class="badge"
              >{{ t('trash.badge.wasLive') }}</span>
              <span
                v-else-if="entry.hadSchedule"
                class="badge"
              >{{ t('trash.badge.wasScheduled') }}</span>
            </th>
            <td>{{ where(entry) }}</td>
            <td>{{ t('trash.deletedBy', { name: entry.trashedBy.displayName, when: when(entry.trashedAtUtc) }) }}</td>
            <td>{{ t('trash.purgeOn', { date: d(new Date(entry.purgeOnUtc), 'short') }) }}</td>
            <td class="actions">
              <button
                type="button"
                :aria-label="t('trash.previewLabel', { label: entry.label })"
                @click="previewing = entry"
              >
                {{ t('trash.preview.action') }}
              </button>
              <button
                v-if="entry.canRestore"
                type="button"
                :aria-label="t('trash.restoreLabel', { label: entry.label })"
                @click="restoring = entry"
              >
                {{ t('trash.restoreAction') }}
              </button>
              <button
                v-if="entry.canPurge"
                type="button"
                class="danger"
                :aria-label="t('trash.purge.label', { label: entry.label })"
                @click="purge(entry)"
              >
                {{ t('trash.purge.action') }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <button
        v-if="next"
        type="button"
        :disabled="loading"
        @click="load(true)"
      >
        {{ t('trash.more') }}
      </button>
    </div>

    <TrashPreview
      v-if="previewing"
      :site-id="siteId"
      :entry="previewing"
      @close="previewing = null"
      @restore="restoring = previewing; previewing = null"
    />
    <EmptyTrashDialog
      v-if="emptying"
      :site-id="siteId"
      @emptied="emptied"
      @cancel="emptying = false"
    />
    <RestoreDialog
      v-if="restoring"
      :site-id="siteId"
      :entry="restoring"
      @restored="restored"
      @cancel="restoring = null"
    />
  </section>
</template>

<style scoped>
/* One tab per kind, underlined like a section switch rather than boxed like buttons. */
.trash-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  margin: 1rem 0 1.25rem;
  border-bottom: 1px solid var(--border);
}

.trash-tabs [role='tab'] {
  margin-bottom: -1px;
  padding: 0.5rem 0.875rem;
  border: 0;
  border-bottom: 3px solid transparent;
  border-radius: 0;
  background: none;
  color: var(--text-muted);
}

.trash-tabs [role='tab']:hover {
  color: var(--text);
}

.trash-tabs [role='tab'][aria-selected='true'] {
  border-bottom-color: var(--accent);
  color: var(--text);
  font-weight: 600;
}

/* Search takes the spare room; the filters beside it are as wide as their content (as in Media). */
.trash-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem 1rem;
  margin-bottom: 1rem;
}

.trash-toolbar .field {
  margin-bottom: 0;
}

.trash-toolbar__search {
  flex: 1 1 16rem;
  max-width: 28rem;
}

.trash-toolbar select,
.trash-toolbar input[type='date'] {
  width: auto;
}

.trash-toolbar :is(input, select) {
  height: 2.5rem;
}

.trash-bulk {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.sites-table .actions {
  justify-content: flex-end;
}
</style>
