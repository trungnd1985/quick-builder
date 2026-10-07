<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { cmsApi, type CollectionList, type CollectionSummary } from '../../api/cms'
import { useSitesStore } from '../../app/sitesStore'
import { suggestKey } from './collectionSchema'
import { cmsErrorMessage } from './cmsErrors'
import DeleteCollectionDialog from './DeleteCollectionDialog.vue'

// 014 US1, US5: a site's collections — list them, create one, delete one. Editors see the
// list read-only (spec Assumptions); the server checks every action regardless.

const { t, te, d } = useI18n()
const route = useRoute()
const router = useRouter()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const list = ref<CollectionList | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const name = ref('')
const slug = ref('')
const slugEdited = ref(false)
const description = ref('')
const creating = ref(false)
const createError = ref<string | null>(null)
const pendingDelete = ref<CollectionSummary | null>(null)
const status = ref('')

const collections = computed(() => list.value?.collections ?? [])
const canManage = computed(() => list.value?.canManage === true)

watch(name, (value) => {
  if (!slugEdited.value) slug.value = suggestKey(value)
})

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
    list.value = await cmsApi.list(siteId.value)
  } catch (caught) {
    loadError.value = cmsErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

async function create(): Promise<void> {
  creating.value = true
  createError.value = null
  status.value = ''

  try {
    const created = await cmsApi.create(siteId.value, { name: name.value, slug: slug.value, description: description.value || null })
    status.value = t('cms.status.created', { name: created.name })
    await router.push({ name: 'site-collection', params: { siteId: siteId.value, collectionId: created.id } })
  } catch (caught) {
    createError.value = cmsErrorMessage(t, te, caught)
  } finally {
    creating.value = false
  }
}

async function deleted(collection: CollectionSummary): Promise<void> {
  pendingDelete.value = null
  status.value = t('cms.status.deleted', { name: collection.name })
  await load()
}
</script>

<template>
  <section aria-labelledby="collections-heading">
    <p>
      <RouterLink :to="{ name: 'site', params: { siteId } }">
        {{ t('cms.list.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="collections-heading">
          {{ t('cms.list.heading') }}
        </h1>
        <p class="subtitle">
          {{ t('cms.list.intro', { site: siteName }) }}
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
      {{ t('cms.list.loading') }}
    </p>

    <div
      v-else-if="loadError && !list"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('cms.list.errorTitle') }}</h2>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('cms.list.retry') }}
      </button>
    </div>

    <template v-else-if="list">
      <p
        v-if="!canManage"
        class="help"
      >
        {{ t('cms.list.readOnly') }}
      </p>

      <form
        v-if="canManage"
        class="card"
        aria-labelledby="collection-create-heading"
        novalidate
        @submit.prevent="create"
      >
        <h2 id="collection-create-heading">
          {{ t('cms.create.heading') }}
        </h2>
        <div class="field">
          <label for="collection-create-name">{{ t('cms.create.name') }}</label>
          <input
            id="collection-create-name"
            v-model="name"
            type="text"
            maxlength="100"
            required
          >
        </div>
        <div class="field">
          <label for="collection-create-slug">{{ t('cms.create.slug') }}</label>
          <input
            id="collection-create-slug"
            v-model="slug"
            type="text"
            maxlength="64"
            required
            aria-describedby="collection-create-slug-help"
            @input="slugEdited = true"
          >
          <p
            id="collection-create-slug-help"
            class="help"
          >
            {{ t('cms.create.slugHelp') }}
          </p>
        </div>
        <div class="field">
          <label for="collection-create-description">{{ t('cms.create.description') }}</label>
          <textarea
            id="collection-create-description"
            v-model="description"
            maxlength="500"
            rows="2"
          />
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
          {{ creating ? t('cms.create.creating') : t('cms.create.create') }}
        </button>
      </form>

      <p
        v-if="collections.length === 0"
        class="card"
      >
        {{ t('cms.list.empty') }}
      </p>

      <table
        v-else
        class="sites-table"
      >
        <caption class="visually-hidden">
          {{ t('cms.list.table.caption', { site: siteName }) }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('cms.list.table.name') }}
            </th>
            <th scope="col">
              {{ t('cms.list.table.slug') }}
            </th>
            <th scope="col">
              {{ t('cms.list.table.fields') }}
            </th>
            <th scope="col">
              {{ t('cms.list.table.items') }}
            </th>
            <th scope="col">
              {{ t('cms.list.table.updated') }}
            </th>
            <th
              v-if="canManage"
              scope="col"
            >
              {{ t('cms.list.table.actions') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="collection in collections"
            :key="collection.id"
          >
            <th scope="row">
              <RouterLink :to="{ name: 'site-collection', params: { siteId, collectionId: collection.id } }">
                {{ collection.name }}
              </RouterLink>
            </th>
            <td><code>{{ collection.slug }}</code></td>
            <td>{{ collection.fieldCount }}</td>
            <td>{{ collection.itemCount }}</td>
            <td>{{ d(new Date(collection.updatedAtUtc), 'short') }}</td>
            <td v-if="canManage">
              <button
                type="button"
                class="danger"
                :aria-label="t('cms.list.table.delete', { name: collection.name })"
                @click="pendingDelete = collection"
              >
                {{ t('cms.builder.delete') }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </template>

    <DeleteCollectionDialog
      v-if="pendingDelete"
      :site-id="siteId"
      :collection-id="pendingDelete.id"
      :name="pendingDelete.name"
      :revision="pendingDelete.revision"
      :item-count="pendingDelete.itemCount"
      @deleted="deleted(pendingDelete)"
      @cancel="pendingDelete = null"
    />
  </section>
</template>
