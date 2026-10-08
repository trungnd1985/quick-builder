<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, mediaApi, type MediaFile, type MediaFolder, type MediaKind, type MediaListQuery, type MediaSort } from '../../api/media'
import DeleteDialog from './DeleteDialog.vue'
import FolderTree from './FolderTree.vue'
import MediaList from './MediaList.vue'
import UploadArea from './UploadArea.vue'

// 013 US1 and US4: one component for the library screen and the editor's picker (MV-23).
// Folders on the side; search, kind, sort and direction above the list; upload into the
// folder shown. Without a search it lists the folder; with one, the whole library, or the
// folder and everything under it when "Only in this folder" is ticked (FR-005, FR-006).
// Loading, error, empty and no-results are distinct states (001 FR-045).

const props = withDefaults(
  defineProps<{
    siteId: string
    mode?: 'library' | 'picker'
    /** The folder to open first; null is the top level. */
    initialFolderId?: string | null
    hostNames?: string[]
    /** Whether the member may upload, file and organise (the upload capability). */
    canUpload?: boolean
    /** The kind shown first; by default images in a picker, everything in the library (015: file fields choose documents). */
    initialKind?: MediaKind | ''
  }>(),
  { mode: 'library', initialFolderId: null, hostNames: () => [], canUpload: true, initialKind: undefined },
)

const emit = defineEmits<{
  choose: [file: MediaFile]
  details: [file: MediaFile, trigger: HTMLElement]
  folderChange: [folderId: string | null]
  deleted: [file: MediaFile]
}>()

const { t, te } = useI18n()

const SEARCH_DELAY_MS = 250

const folderId = ref<string | null>(props.initialFolderId)
const search = ref('')
const appliedSearch = ref('')
const onlyThisFolder = ref(false)
const kind = ref<MediaKind | ''>(props.initialKind ?? (props.mode === 'picker' ? 'image' : ''))
const sort = ref<MediaSort>('uploaded')
const direction = ref<'' | 'asc' | 'desc'>('')

const files = ref<MediaFile[]>([])
const totalCount = ref(0)
const nextCursor = ref<string | null>(null)
const loading = ref(true)
const loadingMore = ref(false)
const error = ref<string | null>(null)

const selectedIds = ref<string[]>([])
const moveTarget = ref('')
const moving = ref(false)
const folders = ref<MediaFolder[]>([])

const list = ref<InstanceType<typeof MediaList> | null>(null)
const tree = ref<InstanceType<typeof FolderTree> | null>(null)
const pendingDelete = ref<MediaFile | null>(null)
const deleteTrigger = ref<HTMLElement | null>(null)
const deleting = ref(false)
const statusText = ref('')

let searchTimer: ReturnType<typeof setTimeout> | undefined
let generation = 0

const searching = computed(() => appliedSearch.value.length > 0)

const query = computed<MediaListQuery>(() => ({
  folderId: folderId.value,
  q: appliedSearch.value || undefined,
  scope: searching.value && onlyThisFolder.value && folderId.value ? 'subtree' : 'folder',
  kind: kind.value || undefined,
  sort: sort.value,
  direction: direction.value || undefined,
}))

onMounted(async () => {
  await Promise.all([loadFirstPage(), loadFolders()])
})

onBeforeUnmount(() => clearTimeout(searchTimer))

watch(search, (value) => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    appliedSearch.value = value.trim()
  }, SEARCH_DELAY_MS)
})

watch([appliedSearch, onlyThisFolder, kind, sort, direction], () => void loadFirstPage())

async function loadFolders(): Promise<void> {
  try {
    folders.value = (await mediaApi.folders.list(props.siteId)).folders
  } catch {
    folders.value = []
  }
}

async function loadFirstPage(): Promise<void> {
  const mine = ++generation
  loading.value = true
  error.value = null
  selectedIds.value = []

  try {
    const page = await mediaApi.list(props.siteId, query.value)
    if (mine !== generation) return
    files.value = page.items
    totalCount.value = page.totalCount
    nextCursor.value = page.nextCursor
  } catch (caught) {
    if (mine !== generation) return

    // A remembered folder deleted since (013 US4): the top level instead of a dead end.
    if (caught instanceof ApiError && caught.errorCode === 'media.folder_not_found' && folderId.value !== null) {
      openFolder(null)
      return
    }

    error.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    if (mine === generation) loading.value = false
  }
}

async function loadMore(): Promise<void> {
  if (!nextCursor.value) {
    return
  }

  loadingMore.value = true
  try {
    const page = await mediaApi.list(props.siteId, { ...query.value, cursor: nextCursor.value })
    // Keyset paging: files uploaded meanwhile sit before the cursor, so nothing repeats.
    const known = new Set(files.value.map((f) => f.id))
    const added = page.items.filter((f) => !known.has(f.id))
    files.value = [...files.value, ...added]
    totalCount.value = page.totalCount
    nextCursor.value = page.nextCursor

    const first = added[0]
    if (first) {
      await list.value?.focusRow(first.id)
    }
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    loadingMore.value = false
  }
}

function openFolder(id: string | null): void {
  folderId.value = id
  emit('folderChange', id)
  void loadFirstPage()
}

function searchEverywhere(): void {
  onlyThisFolder.value = false
}

function clearSearch(): void {
  search.value = ''
  appliedSearch.value = ''
}

function onUploaded(file: MediaFile): void {
  // It belongs in the list shown only when browsing (not searching) the folder it went to.
  if (!searching.value && (file.folderId ?? null) === folderId.value && (!kind.value || kindOfFile(file) === kind.value)) {
    files.value = [file, ...files.value]
    totalCount.value += 1
  }
}

function kindOfFile(file: MediaFile): MediaKind {
  return file.contentType.startsWith('image/') ? 'image' : 'document'
}

function onToggle(fileId: string, selected: boolean): void {
  selectedIds.value = selected ? [...selectedIds.value, fileId] : selectedIds.value.filter((id) => id !== fileId)
}

async function moveSelected(): Promise<void> {
  if (selectedIds.value.length === 0) return
  moving.value = true
  const target = moveTarget.value === '' ? null : moveTarget.value
  try {
    await mediaApi.move(props.siteId, selectedIds.value, target)
    statusText.value = t('media.move.done', { n: selectedIds.value.length }, selectedIds.value.length)
    await Promise.all([loadFirstPage(), tree.value?.reload()])
  } catch (caught) {
    statusText.value = caught instanceof ApiError && caught.errorCode === 'media.folder_not_found'
      ? t('media.folders.errors.folder_not_found')
      : t('media.move.failed')
  } finally {
    moving.value = false
  }
}

function askToDelete(file: MediaFile, trigger: HTMLElement): void {
  pendingDelete.value = file
  deleteTrigger.value = trigger
}

async function closeDialog(): Promise<void> {
  pendingDelete.value = null
  deleting.value = false
  await nextTick()
  deleteTrigger.value?.focus()
}

async function confirmDelete(options: { confirmInUse: boolean }): Promise<void> {
  const file = pendingDelete.value
  if (!file) {
    return
  }

  deleting.value = true
  try {
    await mediaApi.remove(props.siteId, file.id, options)
    files.value = files.value.filter((f) => f.id !== file.id)
    totalCount.value = Math.max(0, totalCount.value - 1)
    list.value?.announce(t('media.delete.done', { name: file.fileName }))

    deleteTrigger.value = null
    pendingDelete.value = null
    deleting.value = false
    emit('deleted', file)
  } catch (caught) {
    const code = caught instanceof ApiError ? caught.errorCode : undefined

    // 013 FR-015: something started using it while the dialog was open. Show the usage and ask again.
    if (code === 'media.in_use' && caught instanceof ApiError && caught.problem?.usage) {
      const updated = { ...file, usage: caught.problem.usage }
      pendingDelete.value = updated
      update(updated)
      deleting.value = false
      return
    }

    const key = code?.startsWith('media.') ? `media.errors.${code.slice('media.'.length)}` : undefined
    list.value?.announce(key && te(key) ? t(key) : t('media.delete.failed', { name: file.fileName }))
    await closeDialog()
  }
}

function folderPath(folder: MediaFolder): string {
  const names = [folder.name]
  let parent = folder.parentId
  while (parent) {
    const next = folders.value.find((f) => f.id === parent)
    if (!next) break
    names.unshift(next.name)
    parent = next.parentId
  }

  return names.join(' / ')
}

/** Replaces one file in the list, after it changed elsewhere (details panel). */
function update(file: MediaFile): void {
  files.value = files.value.map((f) => (f.id === file.id ? file : f))
}

defineExpose({ reload: loadFirstPage, update })
</script>

<template>
  <div
    class="media-browser"
    :class="`media-browser--${mode}`"
  >
    <FolderTree
      ref="tree"
      :site-id="siteId"
      :selected-id="folderId"
      :can-edit="mode === 'library' && canUpload"
      @select="openFolder"
      @changed="loadFolders"
    />

    <div class="media-browser__main">
      <form
        class="media-toolbar"
        role="search"
        @submit.prevent
      >
        <label class="field">
          <span>{{ t('media.search.label') }}</span>
          <input
            v-model="search"
            type="search"
            maxlength="100"
            :placeholder="t('media.search.placeholder')"
          >
        </label>

        <label
          v-if="searching && folderId"
          class="checkbox"
        >
          <input
            v-model="onlyThisFolder"
            type="checkbox"
          >
          <span>{{ t('media.search.onlyThisFolder') }}</span>
        </label>

        <label
          v-if="mode === 'library'"
          class="field"
        >
          <span>{{ t('media.filter.kind') }}</span>
          <select v-model="kind">
            <option value="">{{ t('media.filter.all') }}</option>
            <option value="image">{{ t('media.filter.images') }}</option>
            <option value="document">{{ t('media.filter.documents') }}</option>
          </select>
        </label>

        <label class="field">
          <span>{{ t('media.sort.label') }}</span>
          <select v-model="sort">
            <option value="uploaded">{{ t('media.sort.uploaded') }}</option>
            <option value="name">{{ t('media.sort.name') }}</option>
            <option value="size">{{ t('media.sort.size') }}</option>
          </select>
        </label>

        <label class="field">
          <span>{{ t('media.sort.direction') }}</span>
          <select v-model="direction">
            <option value="">{{ t('media.sort.default') }}</option>
            <option value="asc">{{ t('media.sort.asc') }}</option>
            <option value="desc">{{ t('media.sort.desc') }}</option>
          </select>
        </label>
      </form>

      <UploadArea
        v-if="canUpload"
        :site-id="siteId"
        :folder-id="folderId"
        :accept="mode === 'picker' ? 'image' : undefined"
        @uploaded="(file: MediaFile) => { onUploaded(file); if (mode === 'picker') emit('choose', file) }"
      />

      <p
        class="visually-hidden"
        role="status"
      >
        {{ statusText }}
      </p>

      <p
        v-if="loading"
        class="status"
        role="status"
      >
        {{ t('media.loading') }}
      </p>

      <div
        v-else-if="error"
        class="card error-card"
        role="alert"
      >
        <h2>{{ t('media.error.title') }}</h2>
        <p>{{ error }}</p>
        <button
          type="button"
          @click="loadFirstPage()"
        >
          {{ t('media.error.retry') }}
        </button>
      </div>

      <div
        v-else-if="files.length === 0 && searching"
        class="card empty"
      >
        <h2>{{ t('media.search.none') }}</h2>
        <div class="actions">
          <button
            v-if="onlyThisFolder"
            type="button"
            @click="searchEverywhere"
          >
            {{ t('media.search.everywhere') }}
          </button>
          <button
            type="button"
            @click="clearSearch"
          >
            {{ t('media.search.clear') }}
          </button>
        </div>
      </div>

      <div
        v-else-if="files.length === 0"
        class="card empty"
      >
        <h2>{{ folderId ? t('media.empty.folderTitle') : t('media.empty.title') }}</h2>
        <p>{{ t('media.empty.body') }}</p>
      </div>

      <template v-else>
        <p class="help">
          {{ t('media.count', { shown: files.length, total: totalCount }) }}
        </p>

        <div
          v-if="mode === 'library' && canUpload && selectedIds.length > 0"
          class="move-bar"
        >
          <label class="field">
            <span>{{ t('media.move.label', { n: selectedIds.length }, selectedIds.length) }}</span>
            <select v-model="moveTarget">
              <option value="">{{ t('media.folders.topLevel') }}</option>
              <option
                v-for="folder in folders"
                :key="folder.id"
                :value="folder.id"
              >{{ folderPath(folder) }}</option>
            </select>
          </label>
          <button
            type="button"
            class="primary"
            :disabled="moving"
            @click="moveSelected"
          >
            {{ t('media.move.button') }}
          </button>
        </div>

        <MediaList
          ref="list"
          :site-id="siteId"
          :files="files"
          :host-names="hostNames"
          :mode="mode"
          :selected-ids="selectedIds"
          :can-move="canUpload"
          @delete="askToDelete"
          @details="(file, trigger) => emit('details', file, trigger)"
          @choose="(file) => emit('choose', file)"
          @toggle="onToggle"
        />

        <div
          v-if="nextCursor"
          class="actions"
        >
          <button
            type="button"
            :disabled="loadingMore"
            @click="loadMore"
          >
            {{ t('media.loadMore') }}
          </button>
        </div>
      </template>
    </div>

    <DeleteDialog
      v-if="pendingDelete"
      :site-id="siteId"
      :file="pendingDelete"
      :busy="deleting"
      @cancel="closeDialog"
      @confirm="confirmDelete"
    />
  </div>
</template>
