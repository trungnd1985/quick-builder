<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { pagesApi, type PageDetail, type PageNode, type PageTree } from '../../api/pages'
import { useSitesStore } from '../../app/sitesStore'
import PublicationBadge from '../publishing/PublicationBadge.vue'
import PublishMenu from '../publishing/PublishMenu.vue'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import MoveDialog from './MoveDialog.vue'
import PageFormDialog from './PageFormDialog.vue'
import { isOutOfDate, pageErrorMessage } from './pageErrors'

// 010 US1, US2, US3 and US5: one site's page structure. The tree is a table in site order;
// selecting a row brings up one toolbar of actions for that page, so a 2,000-page tree is
// 2,000 rows rather than 14,000 buttons (SC-005). Every action works from the keyboard,
// with no drag-only path (FR-033). What is offered follows the permissions the server
// returns; the server checks every action regardless (FR-030).

type Dialog =
  | { kind: 'create'; parent: PageNode | null }
  | { kind: 'rename'; page: PageNode }
  | { kind: 'move'; page: PageNode }
  | { kind: 'makeHome'; page: PageNode }
  | { kind: 'delete'; page: PageNode }

const { t, te, d } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const tree = ref<PageTree | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const selectedId = ref<string | null>(null)
const detail = ref<PageDetail | null>(null)
const dialog = ref<Dialog | null>(null)
const busy = ref(false)
const dialogError = ref<string | null>(null)
const actionError = ref<string | null>(null)
const outOfDate = ref(false)
const status = ref('')
const toolbarHeading = ref<HTMLElement | null>(null)

const pages = computed(() => tree.value?.pages ?? [])
const canManage = computed(() => tree.value?.permissions.canManageStructure === true)
const canEditDrafts = computed(() => tree.value?.permissions.canEditPageDrafts === true)
const canPublish = computed(() => tree.value?.permissions.canPublish === true)
const hasHome = computed(() => pages.value.some((p) => p.isHome))
const selected = computed(() => pages.value.find((p) => p.id === selectedId.value) ?? null)

const siblings = computed(() =>
  selected.value
    ? pages.value.filter((p) => p.parentId === selected.value!.parentId).sort((a, b) => a.position - b.position)
    : [])
const selectedIndex = computed(() => siblings.value.findIndex((p) => p.id === selectedId.value))

onMounted(async () => {
  if (sites.sites.length === 0) {
    await sites.load().catch(() => undefined)
  }

  await load()
})

watch(siteId, load)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null

  try {
    show(await pagesApi.tree(siteId.value))
    outOfDate.value = false
  } catch (caught) {
    loadError.value = pageErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

function show(next: PageTree): void {
  tree.value = next
  if (selectedId.value && !next.pages.some((p) => p.id === selectedId.value)) {
    selectedId.value = null
    detail.value = null
  }
}

async function select(page: PageNode): Promise<void> {
  selectedId.value = page.id
  actionError.value = null
  detail.value = null

  await nextTick()
  toolbarHeading.value?.focus()

  try {
    detail.value = await pagesApi.get(siteId.value, page.id)
  } catch {
    // The details panel is a courtesy; the actions still work without it.
    detail.value = null
  }
}

async function focusRow(pageId: string | null): Promise<void> {
  await nextTick()
  const target = pageId ? document.getElementById(`page-row-${pageId}`) : null
  target?.focus()
}

function open(next: Dialog): void {
  dialogError.value = null
  dialog.value = next
}

async function close(): Promise<void> {
  dialog.value = null
  await focusRow(selectedId.value)
}

/** Runs one change; refreshes the tree; reports out-of-date as its own state. */
async function run(action: () => Promise<PageTree | void>, message: string, inDialog: boolean): Promise<boolean> {
  busy.value = true
  dialogError.value = null
  actionError.value = null
  status.value = ''

  try {
    const result = await action()
    show(result ?? await pagesApi.tree(siteId.value))
    status.value = message
    return true
  } catch (caught) {
    if (isOutOfDate(caught)) {
      outOfDate.value = true
      dialog.value = null
    } else if (inDialog) {
      dialogError.value = pageErrorMessage(t, te, caught)
    } else {
      actionError.value = pageErrorMessage(t, te, caught)
    }

    return false
  } finally {
    busy.value = false
  }
}

async function submitForm(value: { title: string; slug: string; isHome: boolean }): Promise<void> {
  const current = dialog.value
  if (current?.kind === 'create') {
    let createdId: string | null = null
    const ok = await run(async () => {
      const created = await pagesApi.create(siteId.value, {
        title: value.title,
        slug: value.slug,
        parentId: current.parent?.id ?? null,
        isHome: value.isHome,
      })
      createdId = created.id
    }, t('pages.status.created', { title: value.title.trim() }), true)

    if (ok) {
      dialog.value = null
      selectedId.value = createdId
      await focusRow(createdId)
    }
  } else if (current?.kind === 'rename') {
    const ok = await run(
      async () => { await pagesApi.update(siteId.value, current.page.id, { ...value, version: current.page.version }) },
      t('pages.status.renamed', { title: value.title.trim() }),
      true)

    if (ok) {
      await close()
    }
  }
}

async function submitMove(value: { parentId: string | null; afterPageId: string | null }): Promise<void> {
  const current = dialog.value
  if (current?.kind !== 'move') {
    return
  }

  if (await run(
    () => pagesApi.move(siteId.value, current.page.id, { ...value, version: current.page.version }),
    t('pages.status.moved', { title: current.page.title }),
    true)) {
    await close()
  }
}

async function moveBy(offset: -1 | 1): Promise<void> {
  const page = selected.value
  const index = selectedIndex.value
  if (!page || index < 0) {
    return
  }

  // Name the sibling to follow, never an index (research P-05).
  const after = offset < 0
    ? (index >= 2 ? siblings.value[index - 2]!.id : null)
    : siblings.value[index + 1]?.id ?? null

  await run(
    () => pagesApi.move(siteId.value, page.id, { parentId: page.parentId, afterPageId: after, version: page.version }),
    t('pages.status.moved', { title: page.title }),
    false)
  await focusRow(page.id)
}

async function confirm(): Promise<void> {
  const current = dialog.value
  if (current?.kind === 'makeHome') {
    if (await run(
      () => pagesApi.makeHome(siteId.value, current.page.id, current.page.version),
      t('pages.status.madeHome', { title: current.page.title }),
      true)) {
      await close()
    }
  } else if (current?.kind === 'delete') {
    const index = pages.value.findIndex((p) => p.id === current.page.id)
    const previous = index > 0 ? pages.value[index - 1]!.id : null
    if (await run(
      () => pagesApi.remove(siteId.value, current.page.id, current.page.version),
      t('pages.status.deleted', { title: current.page.title }),
      true)) {
      dialog.value = null
      selectedId.value = previous
      await focusRow(previous)
    }
  }
}

function addressPrefix(parent: PageNode | null): string {
  return parent === null || parent.isHome ? '' : parent.path
}

function formPrefix(current: Dialog): string {
  if (current.kind === 'create') {
    return addressPrefix(current.parent)
  }

  const parentId = 'page' in current ? current.page.parentId : null
  return addressPrefix(pages.value.find((p) => p.id === parentId) ?? null)
}

function formHeading(current: Dialog): string {
  if (current.kind === 'rename') {
    return t('pages.form.renameTitle', { title: current.page.title })
  }

  return current.kind === 'create' && current.parent
    ? t('pages.form.createChildTitle', { parent: current.parent.title })
    : t('pages.form.createTitle')
}

function whoSaved(value: PageDetail): string {
  return value.draft.updatedBy.displayName ?? t('pages.details.unknownUser')
}
</script>

<template>
  <section aria-labelledby="pages-heading">
    <p>
      <RouterLink :to="{ name: 'site', params: { siteId } }">
        {{ t('pages.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="pages-heading">
          {{ t('pages.heading') }}
        </h1>
        <p class="subtitle">
          {{ t('pages.intro', { site: siteName }) }}
        </p>
      </div>
      <button
        v-if="canManage"
        type="button"
        class="primary"
        @click="open({ kind: 'create', parent: null })"
      >
        {{ t('pages.actions.addPage') }}
      </button>
    </header>

    <p
      class="visually-hidden"
      role="status"
      aria-live="polite"
    >
      {{ status }}
    </p>

    <p
      v-if="loading && !tree"
      class="status"
      role="status"
    >
      {{ t('pages.loading') }}
    </p>

    <div
      v-else-if="loadError && !tree"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('pages.errorTitle') }}</h2>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('pages.retry') }}
      </button>
    </div>

    <template v-else-if="tree">
      <p
        v-if="!canManage"
        class="help"
      >
        {{ t('pages.readOnly') }}
      </p>

      <div
        v-if="outOfDate"
        class="card error"
        role="alert"
      >
        <p>{{ t('pages.outOfDate') }}</p>
        <button
          type="button"
          @click="load()"
        >
          {{ t('pages.reload') }}
        </button>
      </div>

      <p
        v-if="pages.length === 0"
        class="card"
      >
        {{ t('pages.empty') }}
      </p>

      <template v-else>
        <div
          class="card"
          aria-labelledby="page-toolbar-heading"
          role="region"
        >
          <h2
            id="page-toolbar-heading"
            ref="toolbarHeading"
            tabindex="-1"
          >
            {{ selected ? t('pages.actions.heading', { title: selected.title }) : t('pages.actions.none') }}
          </h2>

          <!-- 011 FR-001, FR-039: anyone who may save page drafts may open the editor. -->
          <p
            v-if="selected && canEditDrafts"
            class="page-edit-link"
          >
            <RouterLink
              class="button-link primary"
              :to="{ name: 'page-editor', params: { siteId, pageId: selected.id } }"
            >
              {{ t('pages.actions.editContent') }}
            </RouterLink>
          </p>

          <!-- 012 FR-039: publishing, for the selected page. -->
          <PublishMenu
            v-if="selected"
            :key="selected.id"
            class="page-publishing"
            :site-id="siteId"
            kind="page"
            :subject-id="selected.id"
            :title="selected.title"
            :publication="selected.publication"
            :can-publish="canPublish"
            @changed="load()"
          />

          <div
            v-if="selected && canManage"
            class="page-toolbar"
          >
            <button
              type="button"
              @click="open({ kind: 'create', parent: selected })"
            >
              {{ t('pages.actions.addChild') }}
            </button>
            <button
              type="button"
              @click="open({ kind: 'rename', page: selected })"
            >
              {{ t('pages.actions.rename') }}
            </button>
            <button
              type="button"
              :disabled="busy || selectedIndex <= 0"
              @click="moveBy(-1)"
            >
              {{ t('pages.actions.moveUp') }}
            </button>
            <button
              type="button"
              :disabled="busy || selectedIndex < 0 || selectedIndex >= siblings.length - 1"
              @click="moveBy(1)"
            >
              {{ t('pages.actions.moveDown') }}
            </button>
            <button
              type="button"
              @click="open({ kind: 'move', page: selected })"
            >
              {{ t('pages.actions.moveTo') }}
            </button>
            <button
              v-if="!selected.isHome && selected.parentId === null"
              type="button"
              @click="open({ kind: 'makeHome', page: selected })"
            >
              {{ t('pages.actions.makeHome') }}
            </button>
            <button
              type="button"
              class="danger"
              @click="open({ kind: 'delete', page: selected })"
            >
              {{ t('pages.actions.delete') }}
            </button>
          </div>

          <p
            v-if="actionError"
            class="error"
            role="alert"
          >
            {{ actionError }}
          </p>

          <div v-if="selected && detail && detail.id === selected.id">
            <h3>{{ t('pages.details.heading') }}</h3>
            <p>
              {{ t('pages.details.schemaVersion', { version: detail.draft.schemaVersion }) }}
            </p>
            <p>
              {{ t('pages.details.lastSaved', { when: d(new Date(detail.draft.updatedAtUtc), 'short'), who: whoSaved(detail) }) }}
            </p>
            <p class="help">
              {{ t('pages.details.editorLater') }}
            </p>
          </div>
        </div>

        <table class="page-tree">
          <caption class="visually-hidden">
            {{ t('pages.table.caption', { site: siteName }) }}
          </caption>
          <thead>
            <tr>
              <th scope="col">
                {{ t('pages.table.title') }}
              </th>
              <th scope="col">
                {{ t('pages.table.address') }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="page in pages"
              :key="page.id"
              :aria-selected="page.id === selectedId ? 'true' : 'false'"
            >
              <th
                scope="row"
                :style="{ paddingInlineStart: `${0.5 + (page.depth - 1) * 1.5}rem` }"
              >
                <span class="page-title">
                  <button
                    :id="`page-row-${page.id}`"
                    type="button"
                    class="link"
                    :aria-pressed="page.id === selectedId ? 'true' : 'false'"
                    :aria-label="t('pages.table.select', { title: page.title })"
                    @click="select(page)"
                  >{{ page.title }}</button>
                  <span class="visually-hidden">{{ t('pages.table.level', { level: page.depth }) }}</span>
                  <span
                    v-if="page.isHome"
                    class="badge"
                  >{{ t('pages.table.home') }}</span>
                  <PublicationBadge :publication="page.publication" />
                </span>
              </th>
              <td class="page-address">
                {{ page.path }}
              </td>
            </tr>
          </tbody>
        </table>
      </template>
    </template>

    <PageFormDialog
      v-if="dialog && (dialog.kind === 'create' || dialog.kind === 'rename')"
      dialog-id="page-form"
      :heading="formHeading(dialog)"
      :submit-label="dialog.kind === 'create' ? t('pages.form.create') : t('pages.form.save')"
      :address-prefix="formPrefix(dialog)"
      :initial-title="dialog.kind === 'rename' ? dialog.page.title : undefined"
      :initial-slug="dialog.kind === 'rename' ? dialog.page.slug : undefined"
      :offer-home="dialog.kind === 'create' && dialog.parent === null && !hasHome"
      :busy="busy"
      :error="dialogError"
      @submit="submitForm"
      @cancel="close"
    />

    <MoveDialog
      v-if="dialog?.kind === 'move'"
      dialog-id="page-move"
      :page="dialog.page"
      :pages="pages"
      :busy="busy"
      :error="dialogError"
      @submit="submitMove"
      @cancel="close"
    />

    <ConfirmDialog
      v-if="dialog?.kind === 'makeHome'"
      dialog-id="page-make-home"
      :title="t('pages.confirm.makeHomeTitle', { title: dialog.page.title })"
      :body="dialogError ?? t('pages.confirm.makeHomeBody', { title: dialog.page.title })"
      :confirm-label="t('pages.confirm.makeHome')"
      :cancel-label="t('pages.confirm.cancel')"
      :busy-label="t('pages.confirm.working')"
      :busy="busy"
      @confirm="confirm"
      @cancel="close"
    />

    <ConfirmDialog
      v-if="dialog?.kind === 'delete'"
      dialog-id="page-delete"
      :title="t('pages.confirm.deleteTitle', { title: dialog.page.title })"
      :body="dialogError ?? t('pages.confirm.deleteBody', { address: dialog.page.path })"
      :confirm-label="t('pages.confirm.delete')"
      :cancel-label="t('pages.confirm.cancel')"
      :busy-label="t('pages.confirm.deleting')"
      :busy="busy"
      danger
      @confirm="confirm"
      @cancel="close"
    />
  </section>
</template>
