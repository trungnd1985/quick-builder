<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { pagesApi, type Component, type ComponentList } from '../../api/pages'
import { useSitesStore } from '../../app/sitesStore'
import PublishMenu from '../publishing/PublishMenu.vue'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import { restorableUntil, useTrashRetention } from '../trash/useTrashRetention'
import { deleteLockHeldMessage } from '../trash/lockHeld'
import MovedToTrashNotice from '../trash/MovedToTrashNotice.vue'
import ComponentUsagesPanel from './ComponentUsagesPanel.vue'
import LockMarker from '../../editing/LockMarker.vue'
import HolderWarning from '../../editing/HolderWarning.vue'
import { usePresenceList } from '../../editing/usePresenceList'
import { isOutOfDate, pageErrorMessage } from './pageErrors'

// 010 US4: a site's shared components — create, rename, see who uses them, delete the
// unused. Editors see the list read-only (clarification Q3). A component in use cannot be
// deleted; the refusal says how many pages use it, and the usages panel says which.

const { t, te, d } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
// 021 FR-002: the delete confirmation says until when the component can be restored.
const retentionDays = useTrashRetention(String(route.params.siteId))
const trashedUntil = ref<string | null>(null)
const trashNote = computed(() => (retentionDays.value ? ' ' + t('trash.restorableUntil', { date: d(restorableUntil(retentionDays.value), 'short') }) : ''))
/** 023 FR-012: who is editing which component. */
const editing = usePresenceList(() => ({ siteId: siteId.value, kind: 'component' }))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const list = ref<ComponentList | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const newName = ref('')
const creating = ref(false)
const createError = ref<string | null>(null)
const renaming = ref<Component | null>(null)
const renameValue = ref('')
const renameError = ref<string | null>(null)
const pendingDelete = ref<Component | null>(null)
const deleteError = ref<string | null>(null)
const busy = ref(false)
const showingUsages = ref<Component | null>(null)
const outOfDate = ref(false)
const status = ref('')

const components = computed(() => list.value?.components ?? [])
const canManage = computed(() => list.value?.permissions.canManageComponents === true)
const canPublish = computed(() => list.value?.permissions.canPublish === true)

onMounted(async () => {
  editing.start()
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
    list.value = await pagesApi.components(siteId.value)
    outOfDate.value = false
  } catch (caught) {
    loadError.value = pageErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

async function create(): Promise<void> {
  creating.value = true
  createError.value = null
  status.value = ''

  try {
    const created = await pagesApi.createComponent(siteId.value, newName.value)
    newName.value = ''
    status.value = t('components.status.created', { name: created.name })
    await load()
  } catch (caught) {
    createError.value = pageErrorMessage(t, te, caught)
  } finally {
    creating.value = false
  }
}

async function startRename(component: Component): Promise<void> {
  renaming.value = component
  renameValue.value = component.name
  renameError.value = null
  await nextTick()
  document.getElementById(`component-rename-${component.id}`)?.focus()
}

async function saveRename(): Promise<void> {
  const component = renaming.value
  if (!component) {
    return
  }

  busy.value = true
  renameError.value = null

  try {
    const saved = await pagesApi.renameComponent(siteId.value, component.id, renameValue.value, component.version)
    renaming.value = null
    status.value = t('components.status.renamed', { name: saved.name })
    await load()
  } catch (caught) {
    if (isOutOfDate(caught)) {
      renaming.value = null
      outOfDate.value = true
    } else {
      renameError.value = pageErrorMessage(t, te, caught)
    }
  } finally {
    busy.value = false
  }
}

function askDelete(component: Component): void {
  deleteError.value = null
  pendingDelete.value = component
}

async function confirmDelete(): Promise<void> {
  const component = pendingDelete.value
  if (!component) {
    return
  }

  busy.value = true
  deleteError.value = null

  try {
    const receipt = await pagesApi.removeComponent(siteId.value, component.id, component.version)
    trashedUntil.value = receipt?.purgeOnUtc ?? null
    pendingDelete.value = null
    status.value = t('components.status.deleted', { name: component.name })
    await load()
  } catch (caught) {
    if (isOutOfDate(caught)) {
      pendingDelete.value = null
      outOfDate.value = true
    } else {
      deleteError.value = deleteLockHeldMessage(t, d, caught) ?? pageErrorMessage(t, te, caught)
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section aria-labelledby="components-heading">
    <p>
      <RouterLink :to="{ name: 'site', params: { siteId } }">
        {{ t('components.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="components-heading">
          {{ t('components.heading') }}
        </h1>
        <p class="subtitle">
          {{ t('components.intro', { site: siteName }) }}
        </p>
      </div>
    </header>

    <MovedToTrashNotice
      v-if="trashedUntil"
      :site-id="siteId"
      :purge-on-utc="trashedUntil"
    />

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
      {{ t('components.loading') }}
    </p>

    <div
      v-else-if="loadError && !list"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('components.errorTitle') }}</h2>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('components.retry') }}
      </button>
    </div>

    <template v-else-if="list">
      <p
        v-if="!canManage"
        class="help"
      >
        {{ t('components.readOnly') }}
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

      <form
        v-if="canManage"
        class="card"
        aria-labelledby="component-create-heading"
        novalidate
        @submit.prevent="create"
      >
        <h2 id="component-create-heading">
          {{ t('components.create.heading') }}
        </h2>
        <div class="field">
          <label for="component-create-name">{{ t('components.create.name') }}</label>
          <input
            id="component-create-name"
            v-model="newName"
            type="text"
            maxlength="100"
            required
          >
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
          {{ creating ? t('components.create.creating') : t('components.create.create') }}
        </button>
      </form>

      <p
        v-if="components.length === 0"
        class="card"
      >
        {{ t('components.empty') }}
      </p>

      <table
        v-else
        class="sites-table"
      >
        <caption class="visually-hidden">
          {{ t('components.table.caption', { site: siteName }) }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('components.table.name') }}
            </th>
            <th scope="col">
              {{ t('components.table.usedBy') }}
            </th>
            <th scope="col">
              {{ t('components.table.publishing') }}
            </th>
            <th scope="col">
              {{ t('components.table.actions') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="component in components"
            :key="component.id"
          >
            <th scope="row">
              <form
                v-if="renaming?.id === component.id"
                novalidate
                :aria-label="t('components.renameHeading', { name: component.name })"
                @submit.prevent="saveRename"
                @keydown.esc.prevent="renaming = null"
              >
                <label
                  class="visually-hidden"
                  :for="`component-rename-${component.id}`"
                >{{ t('components.create.name') }}</label>
                <input
                  :id="`component-rename-${component.id}`"
                  v-model="renameValue"
                  type="text"
                  maxlength="100"
                >
                <button
                  type="submit"
                  class="primary"
                  :disabled="busy"
                >
                  {{ t('components.save') }}
                </button>
                <button
                  type="button"
                  :disabled="busy"
                  @click="renaming = null"
                >
                  {{ t('components.cancel') }}
                </button>
                <p
                  v-if="renameError"
                  class="error"
                  role="alert"
                >
                  {{ renameError }}
                </p>
              </form>
              <template v-else>
                {{ component.name }}
                <LockMarker
                  v-if="editing.holderOf(component.id)"
                  :holder="editing.holderOf(component.id)!"
                />
              </template>
            </th>
            <td>
              <button
                type="button"
                class="link"
                :aria-label="t('components.showUsages', { name: component.name })"
                @click="showingUsages = component"
              >
                {{ t('components.table.usage', { count: component.usageCount }, component.usageCount) }}
              </button>
            </td>
            <td>
              <!-- 012 US2: a component is published once and every page showing it follows. -->
              <PublishMenu
                :site-id="siteId"
                kind="component"
                :subject-id="component.id"
                :title="component.name"
                :publication="component.publication"
                :can-publish="canPublish"
                :editing-by="editing.holderOf(component.id)?.holder.displayName ?? null"
                @changed="load()"
              />
            </td>
            <td>
              <div
                v-if="canManage"
                class="page-toolbar"
              >
                <!-- 011 US7, FR-037: components are built in the same editor as pages. -->
                <RouterLink
                  class="button-link primary"
                  :to="{ name: 'component-editor', params: { siteId, componentId: component.id } }"
                  :aria-label="t('components.edit', { name: component.name })"
                >
                  {{ t('components.editContent') }}
                </RouterLink>
                <button
                  type="button"
                  :aria-label="t('components.rename', { name: component.name })"
                  @click="startRename(component)"
                >
                  {{ t('pages.actions.rename') }}
                </button>
                <button
                  type="button"
                  class="danger"
                  :aria-label="t('components.delete', { name: component.name })"
                  @click="askDelete(component)"
                >
                  {{ t('pages.actions.delete') }}
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>

      <ComponentUsagesPanel
        v-if="showingUsages"
        :key="showingUsages.id"
        :site-id="siteId"
        :component="showingUsages"
        @close="showingUsages = null"
      />
    </template>

    <ConfirmDialog
      v-if="pendingDelete"
      dialog-id="component-delete"
      :title="t('components.confirm.deleteTitle', { name: pendingDelete.name })"
      :body="deleteError ?? t('components.confirm.deleteBody') + trashNote"
      :confirm-label="t('components.confirm.delete')"
      :cancel-label="t('components.confirm.cancel')"
      :busy-label="t('components.confirm.deleting')"
      :busy="busy"
      danger
      @confirm="confirmDelete"
      @cancel="pendingDelete = null"
    >
      <HolderWarning
        v-if="editing.holderOf(pendingDelete.id)"
        :name="editing.holderOf(pendingDelete.id)!.holder.displayName"
        action="delete"
      />
    </ConfirmDialog>
  </section>
</template>
