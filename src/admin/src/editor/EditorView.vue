<script setup lang="ts">
// The visual editor for one page's or one shared component's draft (011 FR-001).
//
// Palette on the left, the canvas in the middle, the property panel and layer view on the
// right. Every change goes through the editor store as an operation; history, autosave and
// the recovery copy all follow from there. Keyboard commands are bound here, on the editor's
// root.
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { onBeforeRouteLeave } from 'vue-router'
import { pagesApi } from '../api/pages'
import { useAuthStore } from '../app/authStore'
import PageCanvas from './canvas/PageCanvas.vue'
import BlockPalette from './palette/BlockPalette.vue'
import PropertyPanel from './panel/PropertyPanel.vue'
import LayerView from './layers/LayerView.vue'
import ConflictBanner from './ConflictBanner.vue'
import RecoveryDialog from './RecoveryDialog.vue'
import EditorTopBar from './EditorTopBar.vue'
import { refusalKey } from './editorErrors'
import { definitionOf } from './blocks/registry'
import type { ContentDocument, ContentNode, DocumentKind } from './schema/schema'
import { CURRENT_SCHEMA_VERSION } from './schema/limits'
import { createAutosave, type Autosave } from './state/autosave'
import { useEditorStore } from './state/editorStore'
import { createHistory, type History } from './state/history'
import { handleEditorKey } from './state/keyboard'
import { recoveryOffer, removeRecovery, writeRecovery, type RecoveryKey, type RecoveryOffer } from './state/recovery'
import { resetSitePages } from './state/useSitePages'
import './editor.css'

const props = defineProps<{ siteId: string; kind: DocumentKind; documentId: string }>()

const { t } = useI18n()
const store = useEditorStore()
const auth = useAuthStore()

const title = ref('')
const announcement = ref('')
const notice = ref('')
const saveState = ref<Autosave['state']['value']>('saved')
const history = shallowRef<History | null>(null)
const offer = shallowRef<RecoveryOffer | null>(null)
const reloading = ref(false)
let autosave: Autosave | null = null
const cleanups: (() => void)[] = []

const backTo = computed(() => ({
  name: props.kind === 'page' ? 'site-pages' : 'site-components',
  params: { siteId: props.siteId },
}))

const recoveryKey = (): RecoveryKey => ({ userId: auth.user?.id ?? '', siteId: props.siteId, kind: props.kind, documentId: props.documentId })

function announce(message: string): void {
  // Cleared first so the same message twice is still read out.
  announcement.value = ''
  setTimeout(() => (announcement.value = message), 30)
}

function refused(code: string): void {
  notice.value = t(refusalKey(code))
  announce(notice.value)
}

const fetchDraft = () => (props.kind === 'page' ? pagesApi.pageDraft(props.siteId, props.documentId) : pagesApi.componentDraft(props.siteId, props.documentId))

/** Keeps a copy of unsaved changes in the browser, half a second after the last one (ED-19). */
function keepRecoveryCopies(): void {
  let timer: ReturnType<typeof setTimeout> | null = null
  const write = () => {
    if (timer) clearTimeout(timer)
    timer = null
    if (!store.root || store.root === store.savedRoot || store.readOnly || !auth.user) return
    const document: ContentDocument = { schemaVersion: CURRENT_SCHEMA_VERSION, root: store.root }
    void writeRecovery({ ...recoveryKey(), document, baseVersion: store.baseVersion })
  }
  const stop = watch(() => store.root, () => {
    if (timer) clearTimeout(timer)
    timer = setTimeout(write, 500)
  })
  window.addEventListener('pagehide', write)
  cleanups.push(() => {
    stop()
    if (timer) clearTimeout(timer)
    window.removeEventListener('pagehide', write)
  })
}

async function open(): Promise<void> {
  close()
  notice.value = ''
  offer.value = null
  resetSitePages()
  await store.load(props.siteId, props.kind, props.documentId)

  if (store.loadState === 'not-found' || store.loadState === 'forbidden') {
    // Nothing to offer for a document that is gone or no longer this member's to edit.
    void removeRecovery(recoveryKey())
    return
  }
  if (store.loadState !== 'ready') return

  history.value = createHistory(store)
  autosave = createAutosave(
    store,
    (document, version) =>
      props.kind === 'page'
        ? pagesApi.savePageDraft(props.siteId, props.documentId, { document, version })
        : pagesApi.saveComponentDraft(props.siteId, props.documentId, { document, version }),
    { onSaved: (saved: ContentNode) => {
      if (store.root === saved) void removeRecovery(recoveryKey())
    } },
  )
  cleanups.push(watch(autosave.state, (state) => (saveState.value = state), { immediate: true }))
  keepRecoveryCopies()

  if (auth.user && store.root && !store.readOnly) {
    offer.value = await recoveryOffer(recoveryKey(), { version: store.baseVersion, root: store.root })
  }

  try {
    title.value = props.kind === 'page'
      ? (await pagesApi.get(props.siteId, props.documentId)).title
      : (await pagesApi.getComponent(props.siteId, props.documentId)).name
  } catch {
    title.value = ''
  }
}

function close(): void {
  autosave?.dispose()
  autosave = null
  history.value?.dispose()
  history.value = null
  while (cleanups.length > 0) cleanups.pop()!()
}

/** Restore: the unsaved changes come back and are saved (US3 #8). */
function restoreRecovery(): void {
  if (!offer.value) return
  store.restore({ root: offer.value.record.document.root, selection: null })
  offer.value = null
  announce(t('editor.recovery.restored'))
}

/** Someone saved since: show the old changes read-only, for copying only. */
function openRecoveryReadOnly(): void {
  if (!offer.value) return
  const record = offer.value.record
  store.setReadOnly({ reason: 'recovery' })
  store.restore({ root: record.document.root, selection: null })
  offer.value = null
}

function discardRecovery(): void {
  offer.value = null
  void removeRecovery(recoveryKey())
}

/** Reload latest: the saved draft replaces what is on screen; the clipboard is kept (ED-15). */
async function reloadLatest(): Promise<void> {
  reloading.value = true
  try {
    const draft = await fetchDraft()
    store.replaceWithDraft((draft.document as unknown as ContentDocument).root, draft.version)
    history.value?.clear()
    autosave?.reset()
    void removeRecovery(recoveryKey())
    announce(t('editor.conflict.reloaded'))
  } catch {
    notice.value = t('editor.errors.server.unexpected')
  } finally {
    reloading.value = false
  }
}

function saveNow(): void {
  void autosave?.saveNow()
}

function retryNow(): void {
  void autosave?.retryNow()
}

function undo(): void {
  history.value?.undo()
}

function redo(): void {
  history.value?.redo()
}

function onKeydown(event: KeyboardEvent): void {
  handleEditorKey(event, {
    store,
    userId: auth.user?.id ?? '',
    announce,
    refused,
    saveNow,
    undo,
    redo,
    t,
  })
}

/** Leaving with anything unsaved asks first (FR-030). */
const unsaved = () => (saveState.value !== 'saved' || store.dirty) && store.readOnly?.reason !== 'recovery'

function onBeforeUnload(event: BeforeUnloadEvent): void {
  if (!unsaved()) return
  event.preventDefault()
  event.returnValue = ''
}

onBeforeRouteLeave(() => (unsaved() ? window.confirm(t('editor.leave.confirm')) : true))

onMounted(() => {
  window.addEventListener('beforeunload', onBeforeUnload)
  void open()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', onBeforeUnload)
  close()
})

watch(() => [props.siteId, props.kind, props.documentId], () => void open())

defineExpose({ autosave: () => autosave, history })
</script>

<template>
  <div
    class="editor"
    @keydown="onKeydown"
  >
    <EditorTopBar
      :title="title || t(kind === 'page' ? 'editor.untitledPage' : 'editor.untitledComponent')"
      :back-to="backTo"
      :back-label="t(kind === 'page' ? 'editor.backToPages' : 'editor.backToComponents')"
      :save-state="saveState"
      :can-save="store.loadState === 'ready' && !store.readOnly"
      @save="saveNow"
      @retry="retryNow"
    >
      <button
        type="button"
        :disabled="!history?.canUndo.value || !!store.readOnly"
        aria-keyshortcuts="Control+Z"
        @click="undo"
      >
        {{ t('editor.history.undo') }}
      </button>
      <button
        type="button"
        :disabled="!history?.canRedo.value || !!store.readOnly"
        aria-keyshortcuts="Control+Shift+Z"
        @click="redo"
      >
        {{ t('editor.history.redo') }}
      </button>
    </EditorTopBar>

    <p
      v-if="store.loadState === 'loading'"
      class="editor-message"
      role="status"
    >
      {{ t('editor.loading') }}
    </p>
    <div
      v-else-if="store.loadState === 'not-found'"
      class="editor-message"
      role="alert"
    >
      <h2>{{ t('editor.notFound.title') }}</h2>
      <p>{{ t('editor.notFound.body') }}</p>
    </div>
    <div
      v-else-if="store.loadState === 'forbidden'"
      class="editor-message"
      role="alert"
    >
      <h2>{{ t('editor.forbidden.title') }}</h2>
      <p>{{ t('editor.forbidden.body') }}</p>
    </div>
    <div
      v-else-if="store.loadState === 'error'"
      class="editor-message"
      role="alert"
    >
      <h2>{{ t('editor.loadError.title') }}</h2>
      <button
        type="button"
        @click="open"
      >
        {{ t('editor.loadError.retry') }}
      </button>
    </div>

    <div
      v-else-if="store.loadState === 'ready'"
      class="editor-body"
    >
      <aside class="editor-left">
        <BlockPalette
          @refused="refused"
          @inserted="(type) => announce(t('editor.announce.inserted', { name: t(definitionOf(type)!.labelKey) }))"
        />
      </aside>

      <section
        class="editor-center"
        :aria-label="t('editor.canvas.region')"
      >
        <RecoveryDialog
          v-if="offer"
          :offer="offer"
          @restore="restoreRecovery"
          @read-only="openRecoveryReadOnly"
          @discard="discardRecovery"
        />
        <ConflictBanner
          v-if="store.readOnly"
          :state="store.readOnly"
          :reloading="reloading"
          @reload="reloadLatest"
        />
        <p
          v-if="notice"
          class="editor-notice"
          role="status"
        >
          {{ notice }}
          <button
            type="button"
            class="link"
            @click="notice = ''"
          >
            {{ t('editor.dismiss') }}
          </button>
        </p>
        <div
          class="editor-canvas-scroll"
          tabindex="0"
          :aria-label="t('editor.canvas.label')"
          :aria-describedby="'editor-keys-help'"
        >
          <PageCanvas @refused="refused" />
        </div>
        <p
          id="editor-keys-help"
          class="visually-hidden"
        >
          {{ t('editor.canvas.keysHelp') }}
        </p>
      </section>

      <aside
        class="editor-right"
        :aria-label="t('editor.sidebar')"
      >
        <PropertyPanel />
        <LayerView @refused="refused" />
      </aside>
    </div>

    <p
      class="visually-hidden editor-announcer"
      aria-live="polite"
    >
      {{ announcement }}
    </p>
    <p
      v-if="store.lastRefusal && !notice"
      class="visually-hidden"
      role="status"
    >
      {{ t(refusalKey(store.lastRefusal.code)) }}
    </p>
  </div>
</template>

<style scoped>
.editor {
  display: grid;
  grid-template-rows: auto 1fr;
  height: 100vh;
  background: var(--surface);
}

.editor-body {
  display: grid;
  grid-template-columns: 240px minmax(0, 1fr) 320px;
  min-height: 0;
}

.editor-left,
.editor-right {
  overflow-y: auto;
  background: var(--bg);
  padding: 1rem;
}

.editor-left {
  border-right: 1px solid var(--border);
}

.editor-right {
  border-left: 1px solid var(--border);
}

.editor-center {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.editor-canvas-scroll {
  flex: 1;
  overflow: auto;
  padding: 2.5rem 1.5rem;
  background: #e5e7eb;
}

.editor-banner {
  margin: 0;
  padding: 0.75rem 1rem;
  background: #fff4e5;
  border-bottom: 1px solid #c08a2a;
  color: #5b3b00;
}

.editor-notice {
  margin: 0;
  padding: 0.5rem 1rem;
  background: #fdecee;
  color: var(--danger);
  border-bottom: 1px solid var(--border);
}

.editor-message {
  padding: 2rem;
}
</style>
