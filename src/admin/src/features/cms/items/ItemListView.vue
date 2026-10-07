<script setup lang="ts">
// A collection's items (015 US3, FR-028 to FR-030): search by name or slug, filters, sortable
// columns and number or date fields, and numbered pages. The search, filters, sort and page are
// kept in the page address, so a view can be reloaded and shared, and the form's "back" link
// returns to it.
import { computed, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { cmsApi, type Collection } from '../../../api/cms'
import { itemsApi, type ItemList, type ListQuery } from '../../../api/items'
import ItemFilters from './ItemFilters.vue'
import ItemPager from './ItemPager.vue'
import ItemStatusBadge from './ItemStatusBadge.vue'
import { itemErrorMessage } from './itemErrors'
import { useItemRefs } from './itemRefs'
import { ITEM_REFS } from './itemRefsKey'
import { fromRoute, isNarrowed, toRoute, type PageSize } from './listQuery'

const { t, te, d } = useI18n()
const route = useRoute()
const router = useRouter()

const siteId = computed(() => String(route.params.siteId))
const collectionId = computed(() => String(route.params.collectionId))
provide(ITEM_REFS, useItemRefs(() => siteId.value))

const SEARCH_DELAY_MS = 300
const COLUMNS = ['name', 'slug', 'status', 'updatedAt'] as const

const collection = ref<Collection | null>(null)
const list = ref<ItemList | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const query = computed<ListQuery>(() => fromRoute(route.query))
const search = ref(query.value.q ?? '')
let searchTimer: ReturnType<typeof setTimeout> | undefined

const fieldSorts = computed(() => (collection.value?.schema.fields ?? []).filter((f) => f.type === 'number' || f.type === 'date'))
const sort = computed(() => query.value.sort ?? 'updatedAt')
const dir = computed(() => query.value.dir ?? 'desc')

function ariaSort(column: string): 'ascending' | 'descending' | 'none' {
  if (sort.value !== column) return 'none'
  return dir.value === 'asc' ? 'ascending' : 'descending'
}

function navigate(next: ListQuery): void {
  void router.replace({ query: toRoute(next) })
}

function sortBy(column: string): void {
  const nextDir = sort.value === column && dir.value === 'asc' ? 'desc' : sort.value === column ? 'asc' : column === 'updatedAt' ? 'desc' : 'asc'
  navigate({ ...query.value, sort: column, dir: nextDir, page: 1 })
}

async function loadCollection(): Promise<void> {
  try {
    collection.value = await cmsApi.get(siteId.value, collectionId.value)
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
  }
}

async function loadItems(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    list.value = await itemsApi.list(siteId.value, collectionId.value, query.value)
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await Promise.all([loadCollection(), loadItems()])
})
watch(() => route.query, () => {
  if (route.name === 'site-collection-items') void loadItems()
})

// The search waits for the member to pause typing.
watch(search, (next) => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => navigate({ ...query.value, q: next.trim() || undefined, page: 1 }), SEARCH_DELAY_MS)
})
onBeforeUnmount(() => clearTimeout(searchTimer))

const itemLink = (itemId: string) => ({
  name: 'site-collection-item',
  params: { siteId: siteId.value, collectionId: collectionId.value, itemId },
  query: route.query,
})
</script>

<template>
  <section
    class="item-list"
    aria-labelledby="item-list-heading"
  >
    <p>
      <RouterLink :to="{ name: 'site-collection', params: { siteId, collectionId } }">
        {{ collection?.name ?? '' }}
      </RouterLink>
    </p>
    <header class="page-header">
      <h1 id="item-list-heading">
        {{ t('cms.items.list.title', { collection: collection?.name ?? '' }) }}
      </h1>
      <div class="page-toolbar">
        <RouterLink
          class="button-link primary"
          :to="{ name: 'site-collection-item-new', params: { siteId, collectionId }, query: route.query }"
        >
          {{ t('cms.items.list.newItem') }}
        </RouterLink>
      </div>
    </header>

    <div class="item-list__controls">
      <label
        for="item-search"
        class="visually-hidden"
      >{{ t('cms.items.list.searchLabel') }}</label>
      <input
        id="item-search"
        v-model="search"
        type="search"
        :placeholder="t('cms.items.list.search')"
      >
      <div
        v-if="fieldSorts.length > 0"
        class="field"
      >
        <label for="item-sort-field">{{ t('cms.items.sort.label') }}</label>
        <select
          id="item-sort-field"
          :value="sort.startsWith('field.') ? sort : ''"
          @change="navigate({ ...query, sort: ($event.target as HTMLSelectElement).value || undefined, dir: 'asc', page: 1 })"
        >
          <option value="">
            —
          </option>
          <option
            v-for="field in fieldSorts"
            :key="field.id"
            :value="`field.${field.id}`"
          >
            {{ field.name }}
          </option>
        </select>
      </div>
    </div>

    <ItemFilters
      v-if="collection"
      :site-id="siteId"
      :fields="collection.schema.fields"
      :query="query"
      @change="navigate"
    />

    <p
      v-if="error"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-else-if="loading && !list"
      role="status"
    >
      {{ t('cms.items.list.loading') }}
    </p>

    <template v-else-if="list">
      <div
        v-if="list.total === 0"
        class="empty"
      >
        <p>{{ isNarrowed(query) ? t('cms.items.list.noResults') : t('cms.items.list.empty') }}</p>
        <button
          v-if="isNarrowed(query)"
          type="button"
          @click="search = ''; navigate({ pageSize: query.pageSize })"
        >
          {{ t('cms.items.list.clearFilters') }}
        </button>
      </div>

      <table
        v-else
        class="sites-table"
        :aria-busy="loading"
      >
        <thead>
          <tr>
            <th
              v-for="column in COLUMNS"
              :key="column"
              scope="col"
              :aria-sort="ariaSort(column)"
            >
              <button
                type="button"
                class="link"
                @click="sortBy(column)"
              >
                {{ t(`cms.items.list.columns.${column === 'updatedAt' ? 'updated' : column}`) }}
              </button>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in list.items"
            :key="item.id"
          >
            <th scope="row">
              <RouterLink :to="itemLink(item.id)">
                {{ item.name ?? t('cms.items.list.untitled', { slug: item.slug }) }}
              </RouterLink>
            </th>
            <td><code>{{ item.slug }}</code></td>
            <td>
              <ItemStatusBadge :item="item" />
            </td>
            <td>
              {{ d(new Date(item.updatedAtUtc), 'short') }}
              <span
                v-if="item.updatedBy.name"
                class="help"
              >{{ t('cms.items.list.by', { name: item.updatedBy.name }) }}</span>
            </td>
          </tr>
        </tbody>
      </table>

      <ItemPager
        v-if="list.total > 0"
        :page="list.page"
        :page-size="list.pageSize"
        :total="list.total"
        @page="(page: number) => navigate({ ...query, page })"
        @page-size="(pageSize: PageSize) => navigate({ ...query, pageSize, page: 1 })"
      />
    </template>
  </section>
</template>
