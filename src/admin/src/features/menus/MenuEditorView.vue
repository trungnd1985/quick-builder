<script setup lang="ts">
// 024 US1 and US3: one menu's entries. The draft is edited in memory — add, edit, remove, and
// move by drag or by keyboard — with undo and redo, and saved explicitly with the draft version
// the member opened (FR-015). Every entry is checked before saving, and each problem is marked
// in the tree (FR-011). Entries whose page or item was deleted are summarised at the top
// (FR-025). Members without pages.menus.manage see the menu read-only.
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { onBeforeRouteLeave, useRoute } from 'vue-router'
import { menusApi, type Menu, type MenuDocument, type MenuEntry, type MenuTargetStatus } from '../../api/menus'
import { pagesApi, type PageNode } from '../../api/pages'
import { useSitesStore } from '../../app/sitesStore'
import { entryProblems, validateMenu } from '../../editor/schema/menuValidator'
import { isOutOfDate } from '../publishing/errorMessages'
import MenuEntryDialog from './MenuEntryDialog.vue'
import MenuEntryTree from './MenuEntryTree.vue'
import { menuErrorMessage } from './menuErrors'
import { addEntry, canAddChild, locate, newEntryId, removeEntry, updateEntry } from './menuOps'
import { useMenuHistory } from './useMenuHistory'

const { t, te } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const menuId = computed(() => String(route.params.menuId))
const hostNames = computed(() => sites.sites.find((s) => s.id === siteId.value)?.hostNames ?? [])

const menu = ref<Menu | null>(null)
const document = ref<MenuDocument | null>(null)
const savedJson = ref('')
const pages = ref<PageNode[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const outOfDate = ref(false)
const selected = ref<string | null>(null)
const announcement = ref('')
/** The entry the dialog edits: an id, `new` for a new top-level entry, or `child:<id>`. */
const dialog = ref<string | null>(null)

const history = useMenuHistory(document)

const readOnly = computed(() => menu.value?.permissions.canManage !== true)
const dirty = computed(() => document.value !== null && JSON.stringify(document.value) !== savedJson.value)
const problems = computed(() => (document.value ? entryProblems(document.value) : new Map<string, string>()))
const states = computed(() => new Map<string, MenuTargetStatus>((menu.value?.targets ?? []).map((s) => [s.id, s])))
const pageTitles = computed(() => new Map(pages.value.map((p) => [p.id, p.title])))
const brokenCount = computed(() => {
  if (!document.value) return 0
  let count = 0
  const walk = (entries: MenuEntry[]) => entries.forEach((e) => {
    const id = e.target.type === 'page' ? e.target.pageId : e.target.type === 'item' ? e.target.itemId : null
    if (id && states.value.get(id)?.state === 'deleted') count++
    walk(e.children)
  })
  walk(document.value.entries)
  return count
})

const editing = computed<MenuEntry | null>(() =>
  dialog.value && dialog.value !== 'new' && !dialog.value.startsWith('child:') && document.value
    ? locate(document.value, dialog.value)?.entry ?? null
    : null)

const editingItemName = computed(() => {
  const target = editing.value?.target
  return target?.type === 'item' ? states.value.get(target.itemId)?.title ?? null : null
})

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  window.addEventListener('beforeunload', onBeforeUnload)
  await load()
})

onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))

onBeforeRouteLeave(() => (dirty.value ? window.confirm(t('menus.editor.leaveWarning')) : true))

function onBeforeUnload(event: BeforeUnloadEvent): void {
  if (dirty.value) event.preventDefault()
}

watch(menuId, load)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    const [loaded, tree] = await Promise.all([
      menusApi.get(siteId.value, menuId.value),
      pagesApi.tree(siteId.value).catch(() => ({ pages: [] as PageNode[] })),
    ])
    menu.value = loaded
    document.value = structuredClone(loaded.draft)
    savedJson.value = JSON.stringify(loaded.draft)
    pages.value = tree.pages
    history.reset()
    outOfDate.value = false
  } catch (caught) {
    loadError.value = menuErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

function announce(message: string): void {
  // Cleared first, so the same message twice is announced twice.
  announcement.value = ''
  requestAnimationFrame(() => (announcement.value = message))
}

function onDialogSave(fields: Omit<MenuEntry, 'id' | 'children'>): void {
  const current = document.value
  const opened = dialog.value
  if (!current || !opened) return

  if (opened === 'new' || opened.startsWith('child:')) {
    const id = newEntryId(current)
    const parentId = opened === 'new' ? null : opened.slice('child:'.length)
    history.change(addEntry(current, { id, ...fields, children: [] }, parentId))
    selected.value = id
  } else {
    history.change(updateEntry(current, opened, fields))
  }
  dialog.value = null
}

function removeSelected(id: string): void {
  if (!document.value) return
  const found = locate(document.value, id)
  if (!found) return
  history.change(removeEntry(document.value, id))
  announce(t('menus.tree.removed', { label: found.entry.label || found.entry.id }))
  selected.value = found.siblings[found.index + 1]?.id ?? found.siblings[found.index - 1]?.id ?? found.parent?.id ?? null
}

function onKeydown(event: KeyboardEvent): void {
  if (readOnly.value || !(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== 'z') return
  const done = event.shiftKey ? history.redo() : history.undo()
  if (done) {
    event.preventDefault()
    announce(event.shiftKey ? t('menus.tree.redone') : t('menus.tree.undone'))
  }
}

async function save(): Promise<void> {
  const current = document.value
  const loaded = menu.value
  if (!current || !loaded) return
  saveError.value = null

  // FR-011: nothing the server is certain to refuse is sent.
  if (problems.value.size > 0 || !validateMenu(current).valid) {
    saveError.value = t('menus.editor.problems')
    return
  }

  saving.value = true
  try {
    const saved = await menusApi.saveDraft(siteId.value, loaded.id, current, loaded.draftVersion)
    menu.value = saved
    document.value = structuredClone(saved.draft)
    savedJson.value = JSON.stringify(saved.draft)
    history.reset()
    announce(t('menus.status.saved'))
  } catch (caught) {
    if (isOutOfDate(caught)) outOfDate.value = true
    else saveError.value = menuErrorMessage(t, te, caught)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section
    aria-labelledby="menu-editor-heading"
    @keydown="onKeydown"
  >
    <p>
      <RouterLink :to="{ name: 'site-menus', params: { siteId } }">
        {{ t('menus.editor.back') }}
      </RouterLink>
    </p>

    <p
      class="visually-hidden"
      role="status"
      aria-live="polite"
    >
      {{ announcement }}
    </p>

    <p
      v-if="loading && !menu"
      class="status"
      role="status"
    >
      {{ t('menus.editor.loading') }}
    </p>

    <div
      v-else-if="loadError && !menu"
      class="card error-card"
      role="alert"
    >
      <h1 id="menu-editor-heading">
        {{ t('menus.errorTitle') }}
      </h1>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('menus.retry') }}
      </button>
    </div>

    <template v-else-if="menu && document">
      <header class="page-header">
        <div>
          <h1 id="menu-editor-heading">
            {{ t('menus.editor.heading', { name: menu.name }) }}
          </h1>
          <p
            v-if="!readOnly"
            class="subtitle"
          >
            {{ dirty ? t('menus.editor.unsaved') : t('menus.editor.saved') }}
          </p>
        </div>
        <div
          v-if="!readOnly"
          class="page-toolbar"
        >
          <button
            type="button"
            :disabled="!history.canUndo.value"
            @click="history.undo() && announce(t('menus.tree.undone'))"
          >
            {{ t('menus.tree.undo') }}
          </button>
          <button
            type="button"
            :disabled="!history.canRedo.value"
            @click="history.redo() && announce(t('menus.tree.redone'))"
          >
            {{ t('menus.tree.redo') }}
          </button>
          <button
            type="button"
            class="primary"
            :disabled="saving || !dirty"
            @click="save"
          >
            {{ saving ? t('menus.editor.saving') : t('menus.editor.save') }}
          </button>
        </div>
      </header>

      <p
        v-if="readOnly"
        class="help"
      >
        {{ t('menus.editor.readOnly') }}
      </p>

      <div
        v-if="outOfDate"
        class="card error"
        role="alert"
      >
        <p>{{ t('menus.outOfDate') }}</p>
        <button
          type="button"
          @click="load()"
        >
          {{ t('menus.reload') }}
        </button>
      </div>

      <p
        v-if="saveError"
        class="error"
        role="alert"
      >
        {{ saveError }}
      </p>

      <p
        v-if="brokenCount > 0"
        class="card warning"
        role="note"
      >
        {{ t('menus.editor.brokenSummary', { count: brokenCount }, brokenCount) }}
      </p>

      <div
        v-if="!readOnly"
        class="page-toolbar"
        role="toolbar"
        :aria-label="t('menus.editor.treeLabel', { name: menu.name })"
      >
        <button
          type="button"
          class="primary"
          @click="dialog = 'new'"
        >
          {{ t('menus.editor.addEntry') }}
        </button>
        <template v-if="selected && locate(document, selected)">
          <button
            type="button"
            :disabled="!canAddChild(document, selected)"
            @click="dialog = `child:${selected}`"
          >
            {{ t('menus.tree.addChild') }}
          </button>
          <button
            type="button"
            @click="dialog = selected"
          >
            {{ t('menus.tree.edit') }}
          </button>
          <button
            type="button"
            class="danger"
            @click="removeSelected(selected)"
          >
            {{ t('menus.tree.remove') }}
          </button>
        </template>
      </div>

      <p
        v-if="document.entries.length === 0"
        class="card"
      >
        {{ t('menus.editor.empty') }}
      </p>
      <template v-else>
        <p
          v-if="!readOnly"
          id="menu-tree-help"
          class="help"
        >
          {{ t('menus.tree.keysHelp') }}
        </p>
        <MenuEntryTree
          v-model:selected="selected"
          :document="document"
          :states="states"
          :titles="pageTitles"
          :problems="problems"
          :read-only="readOnly"
          :label="t('menus.editor.treeLabel', { name: menu.name })"
          @change="history.change"
          @edit="dialog = $event"
          @remove="removeSelected"
          @announce="announce"
        />
      </template>
    </template>

    <MenuEntryDialog
      v-if="dialog"
      :site-id="siteId"
      :entry="editing"
      :pages="pages"
      :host-names="hostNames"
      :item-name="editingItemName"
      @save="onDialogSave"
      @cancel="dialog = null"
    />
  </section>
</template>
