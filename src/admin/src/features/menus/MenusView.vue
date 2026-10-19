<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { menusApi, type MenuList, type MenuSummary, type MenuUsage } from '../../api/menus'
import { useSitesStore } from '../../app/sitesStore'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import PublishMenu from '../publishing/PublishMenu.vue'
import { menuErrorMessage } from './menuErrors'
import { isOutOfDate } from '../publishing/errorMessages'

// 024 US1 (FR-001 to FR-003): a site's menus — create, rename, duplicate, delete, and open one
// to edit its entries. Editors see the list read-only. Deleting first shows which drafts still
// show the menu; the server refuses it while published pages or components show it.

const { t, te } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const list = ref<MenuList | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const newName = ref('')
const creating = ref(false)
const createError = ref<string | null>(null)
const renaming = ref<MenuSummary | null>(null)
const renameValue = ref('')
const renameError = ref<string | null>(null)
const pendingDelete = ref<MenuSummary | null>(null)
const pendingUsages = ref<MenuUsage[]>([])
const deleteError = ref<string | null>(null)
const actionError = ref<string | null>(null)
const busy = ref(false)
const outOfDate = ref(false)
const status = ref('')

const menus = computed(() => list.value?.items ?? [])
const canManage = computed(() => list.value?.permissions.canManage === true)
const canPublish = computed(() => list.value?.permissions.canPublish === true)

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  await load()
})

watch(siteId, load)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    list.value = await menusApi.list(siteId.value)
    outOfDate.value = false
  } catch (caught) {
    loadError.value = menuErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

async function create(): Promise<void> {
  creating.value = true
  createError.value = null
  status.value = ''
  try {
    const created = await menusApi.create(siteId.value, newName.value)
    newName.value = ''
    status.value = t('menus.status.created', { name: created.name })
    await load()
  } catch (caught) {
    createError.value = menuErrorMessage(t, te, caught)
  } finally {
    creating.value = false
  }
}

async function duplicate(menu: MenuSummary): Promise<void> {
  busy.value = true
  actionError.value = null
  try {
    const copy = await menusApi.duplicate(siteId.value, menu.id, t('menus.actions.duplicateName', { name: menu.name }))
    status.value = t('menus.status.duplicated', { name: copy.name })
    await load()
  } catch (caught) {
    actionError.value = menuErrorMessage(t, te, caught)
  } finally {
    busy.value = false
  }
}

async function startRename(menu: MenuSummary): Promise<void> {
  renaming.value = menu
  renameValue.value = menu.name
  renameError.value = null
  await nextTick()
  document.getElementById(`menu-rename-${menu.id}`)?.focus()
}

async function saveRename(): Promise<void> {
  const menu = renaming.value
  if (!menu) return
  busy.value = true
  renameError.value = null
  try {
    const saved = await menusApi.rename(siteId.value, menu.id, renameValue.value, menu.version)
    renaming.value = null
    status.value = t('menus.status.renamed', { name: saved.name })
    await load()
  } catch (caught) {
    if (isOutOfDate(caught)) {
      renaming.value = null
      outOfDate.value = true
    } else {
      renameError.value = menuErrorMessage(t, te, caught)
    }
  } finally {
    busy.value = false
  }
}

async function askDelete(menu: MenuSummary): Promise<void> {
  deleteError.value = null
  pendingUsages.value = []
  pendingDelete.value = menu
  try {
    pendingUsages.value = (await menusApi.usages(siteId.value, menu.id)).items.filter((u) => u.inDraft)
  } catch {
    // The confirmation still works without the list; the server decides regardless.
  }
}

const deleteBody = computed(() =>
  deleteError.value
  ?? (pendingUsages.value.length > 0
    ? t('menus.confirm.deleteUsedBody', { list: pendingUsages.value.map((u) => u.label).join(', ') })
    : t('menus.confirm.deleteBody')))

async function confirmDelete(): Promise<void> {
  const menu = pendingDelete.value
  if (!menu) return
  busy.value = true
  deleteError.value = null
  try {
    await menusApi.remove(siteId.value, menu.id, menu.version)
    pendingDelete.value = null
    status.value = t('menus.status.deleted', { name: menu.name })
    await load()
  } catch (caught) {
    if (isOutOfDate(caught)) {
      pendingDelete.value = null
      outOfDate.value = true
    } else {
      deleteError.value = menuErrorMessage(t, te, caught)
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section aria-labelledby="menus-heading">
    <p>
      <RouterLink :to="{ name: 'site', params: { siteId } }">
        {{ t('menus.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="menus-heading">
          {{ t('menus.heading') }}
        </h1>
        <p class="subtitle">
          {{ t('menus.intro', { site: siteName }) }}
        </p>
      </div>
    </header>

    <p
      class="visually-hidden"
      role="status"
      aria-live="polite"
    >
      {{ status }}
    </p>

    <p
      v-if="loading && !list"
      class="status"
      role="status"
    >
      {{ t('menus.loading') }}
    </p>

    <div
      v-else-if="loadError && !list"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('menus.errorTitle') }}</h2>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('menus.retry') }}
      </button>
    </div>

    <template v-else-if="list">
      <p
        v-if="!canManage"
        class="help"
      >
        {{ t('menus.readOnly') }}
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
        v-if="actionError"
        class="error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <form
        v-if="canManage"
        class="card"
        aria-labelledby="menu-create-heading"
        novalidate
        @submit.prevent="create"
      >
        <h2 id="menu-create-heading">
          {{ t('menus.create.heading') }}
        </h2>
        <div class="field">
          <label for="menu-create-name">{{ t('menus.create.name') }}</label>
          <input
            id="menu-create-name"
            v-model="newName"
            type="text"
            maxlength="100"
            required
            aria-describedby="menu-create-limit"
          >
          <p
            id="menu-create-limit"
            class="help"
          >
            {{ t('menus.limit', { count: menus.length, limit: list.limit }) }}
          </p>
        </div>
        <p
          v-if="createError"
          class="error"
          role="alert"
        >
          {{ createError }}
        </p>
        <button
          type="submit"
          class="primary"
          :disabled="creating"
        >
          {{ creating ? t('menus.create.creating') : t('menus.create.create') }}
        </button>
      </form>

      <p
        v-if="menus.length === 0"
        class="card"
      >
        {{ t('menus.empty') }}
      </p>

      <table
        v-else
        class="sites-table"
      >
        <caption class="visually-hidden">
          {{ t('menus.table.caption', { site: siteName }) }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('menus.table.name') }}
            </th>
            <th scope="col">
              {{ t('menus.table.entries') }}
            </th>
            <th scope="col">
              {{ t('menus.table.usedBy') }}
            </th>
            <th scope="col">
              {{ t('menus.table.publishing') }}
            </th>
            <th scope="col">
              {{ t('menus.table.actions') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="menu in menus"
            :key="menu.id"
          >
            <th scope="row">
              <form
                v-if="renaming?.id === menu.id"
                novalidate
                :aria-label="t('menus.actions.renameLabel', { name: menu.name })"
                @submit.prevent="saveRename"
                @keydown.esc.prevent="renaming = null"
              >
                <label
                  class="visually-hidden"
                  :for="`menu-rename-${menu.id}`"
                >{{ t('menus.create.name') }}</label>
                <input
                  :id="`menu-rename-${menu.id}`"
                  v-model="renameValue"
                  type="text"
                  maxlength="100"
                >
                <button
                  type="submit"
                  class="primary"
                  :disabled="busy"
                >
                  {{ t('menus.actions.save') }}
                </button>
                <button
                  type="button"
                  :disabled="busy"
                  @click="renaming = null"
                >
                  {{ t('menus.actions.cancel') }}
                </button>
                <p
                  v-if="renameError"
                  class="error"
                  role="alert"
                >
                  {{ renameError }}
                </p>
              </form>
              <RouterLink
                v-else
                :to="{ name: 'site-menu', params: { siteId, menuId: menu.id } }"
              >
                {{ menu.name }}
              </RouterLink>
            </th>
            <td>
              {{ t('menus.table.entryCount', { count: menu.entryCount }, menu.entryCount) }}
              <span
                v-if="menu.brokenCount > 0"
                class="badge danger"
              >{{ t('menus.table.brokenCount', { count: menu.brokenCount }) }}</span>
            </td>
            <td>
              {{ t('menus.table.usage', { count: menu.usedBy }, menu.usedBy) }}
            </td>
            <td>
              <!-- 024 NV-01: a menu is published once and every page showing it follows. -->
              <PublishMenu
                :site-id="siteId"
                kind="menu"
                :subject-id="menu.id"
                :title="menu.name"
                :publication="menu.publication"
                :can-publish="canPublish"
                @changed="load()"
              />
            </td>
            <td>
              <div class="page-toolbar">
                <RouterLink
                  class="button-link primary"
                  :to="{ name: 'site-menu', params: { siteId, menuId: menu.id } }"
                  :aria-label="t('menus.actions.editLabel', { name: menu.name })"
                >
                  {{ t('menus.actions.edit') }}
                </RouterLink>
                <template v-if="canManage">
                  <button
                    type="button"
                    :aria-label="t('menus.actions.renameLabel', { name: menu.name })"
                    @click="startRename(menu)"
                  >
                    {{ t('menus.actions.rename') }}
                  </button>
                  <button
                    type="button"
                    :disabled="busy"
                    :aria-label="t('menus.actions.duplicateLabel', { name: menu.name })"
                    @click="duplicate(menu)"
                  >
                    {{ t('menus.actions.duplicate') }}
                  </button>
                  <button
                    type="button"
                    class="danger"
                    :aria-label="t('menus.actions.deleteLabel', { name: menu.name })"
                    @click="askDelete(menu)"
                  >
                    {{ t('menus.actions.delete') }}
                  </button>
                </template>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </template>

    <ConfirmDialog
      v-if="pendingDelete"
      dialog-id="menu-delete"
      :title="t('menus.confirm.deleteTitle', { name: pendingDelete.name })"
      :body="deleteBody"
      :confirm-label="t('menus.confirm.delete')"
      :cancel-label="t('menus.confirm.cancel')"
      :busy-label="t('menus.confirm.deleting')"
      :busy="busy"
      danger
      @confirm="confirmDelete"
      @cancel="pendingDelete = null"
    />
  </section>
</template>
