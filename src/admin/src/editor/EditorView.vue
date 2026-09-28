<script setup lang="ts">
// The visual editor for one page's or one shared component's draft (011 FR-001).
//
// Palette on the left, the canvas in the middle, the property panel and layer view on the
// right. Every change goes through the editor store as an operation; autosave, undo and the
// recovery copy follow from there. Keyboard commands are bound here, on the editor's root.
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { onBeforeRouteLeave } from 'vue-router'
import { pagesApi } from '../api/pages'
import { useAuthStore } from '../app/authStore'
import PageCanvas from './canvas/PageCanvas.vue'
import BlockPalette from './palette/BlockPalette.vue'
import EditorTopBar from './EditorTopBar.vue'
import { refusalKey, ruleKey } from './editorErrors'
import { definitionOf } from './blocks/registry'
import type { DocumentKind } from './schema/schema'
import { createAutosave, type Autosave } from './state/autosave'
import { useEditorStore } from './state/editorStore'
import { handleEditorKey } from './state/keyboard'

const props = defineProps<{ siteId: string; kind: DocumentKind; documentId: string }>()

const { t } = useI18n()
const store = useEditorStore()
const auth = useAuthStore()

const title = ref('')
const announcement = ref('')
const notice = ref('')
let autosave: Autosave | null = null
let stopStateWatch: (() => void) | null = null
const saveState = ref<Autosave['state']['value']>('saved')

const backTo = computed(() => ({
  name: props.kind === 'page' ? 'site-pages' : 'site-components',
  params: { siteId: props.siteId },
}))

function announce(message: string): void {
  // Cleared first so the same message twice is still read out.
  announcement.value = ''
  setTimeout(() => (announcement.value = message), 30)
}

function refused(code: string): void {
  notice.value = t(refusalKey(code))
  announce(notice.value)
}

const readOnlyMessage = computed(() => {
  const state = store.readOnly
  if (!state) return ''
  if (state.reason === 'invalid' && state.rule) return t('editor.readOnly.invalidRule', { reason: t(ruleKey(state.rule)) })
  return t(`editor.readOnly.${state.reason}`)
})

async function open(): Promise<void> {
  autosave?.dispose()
  stopStateWatch?.()
  notice.value = ''
  await store.load(props.siteId, props.kind, props.documentId)
  if (store.loadState !== 'ready') return

  autosave = createAutosave(store, (document, version) =>
    props.kind === 'page'
      ? pagesApi.savePageDraft(props.siteId, props.documentId, { document, version })
      : pagesApi.saveComponentDraft(props.siteId, props.documentId, { document, version }),
  )
  stopStateWatch = watch(autosave.state, (state) => (saveState.value = state), { immediate: true })

  try {
    title.value = props.kind === 'page'
      ? (await pagesApi.get(props.siteId, props.documentId)).title
      : (await pagesApi.getComponent(props.siteId, props.documentId)).name
  } catch {
    title.value = ''
  }
}

function saveNow(): void {
  void autosave?.saveNow()
}

function retryNow(): void {
  void autosave?.retryNow()
}

function onKeydown(event: KeyboardEvent): void {
  handleEditorKey(event, {
    store,
    userId: auth.user?.id ?? '',
    announce,
    refused,
    saveNow,
    t,
  })
}

/** Leaving with anything unsaved asks first (FR-030). */
const unsaved = () => saveState.value !== 'saved' || store.dirty

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
  autosave?.dispose()
})

watch(() => [props.siteId, props.kind, props.documentId], () => void open())

defineExpose({ autosave: () => autosave })
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
    />

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
        <div
          v-if="store.readOnly"
          class="editor-banner"
          role="alert"
        >
          {{ readOnlyMessage }}
        </div>
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
        <slot name="panel" />
      </aside>
    </div>

    <p
      class="visually-hidden"
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
