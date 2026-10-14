<script setup lang="ts">
// A site's redirects (017 US2, FR-015): search, filter by state, sort by usage, create, change,
// import and export. States come from the server: hidden when live content answers the source
// first, broken when the page or item it sends to is not live (data-model §3.2).
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { pagesApi, type PageNode } from '../../api/pages'
import { redirectsApi, type ListOptions, type Redirect, type RedirectList, type RedirectState } from '../../api/redirects'
import RedirectDialog from './RedirectDialog.vue'
import RedirectImportDialog from './RedirectImportDialog.vue'
import { redirectErrorMessage } from './redirectErrors'

const { t, te, locale } = useI18n()
const route = useRoute()
const siteId = computed(() => String(route.params.siteId))

const list = ref<RedirectList | null>(null)
const pages = ref<PageNode[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const state = ref<RedirectState | ''>('')
const sort = ref<NonNullable<ListOptions['sort']>>('source')
const page = ref(1)

const editing = ref<Redirect | null>(null)
const creating = ref(false)
const importing = ref(false)

const pageCount = computed(() => (list.value ? Math.max(1, Math.ceil(list.value.total / 50)) : 1))

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    list.value = await redirectsApi.list(siteId.value, {
      q: search.value.trim() || undefined,
      state: state.value || undefined,
      sort: sort.value,
      dir: sort.value === 'source' ? 'asc' : 'desc',
      page: page.value,
    })
  } catch (caught) {
    error.value = redirectErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

async function loadPages(): Promise<void> {
  try {
    pages.value = (await pagesApi.tree(siteId.value)).pages
  } catch {
    pages.value = []
  }
}

async function open(redirect: Redirect): Promise<void> {
  editing.value = await redirectsApi.get(siteId.value, redirect.id).catch(() => redirect)
}

function closeDialogs(): void {
  editing.value = null
  creating.value = false
}

async function afterChange(): Promise<void> {
  closeDialogs()
  await load()
}

const describeTarget = (redirect: Redirect) => redirect.targetAddress ?? t('redirects.list.noAddress')
const describeLastUsed = (redirect: Redirect) =>
  redirect.lastUsedAt ? new Date(redirect.lastUsedAt).toLocaleDateString(locale.value) : t('redirects.list.never')

watch([state, sort], () => {
  page.value = 1
  void load()
})

onMounted(() => {
  void load()
  void loadPages()
})
</script>

<template>
  <section aria-labelledby="redirects-heading">
    <header class="page-header">
      <h1 id="redirects-heading">
        {{ t('redirects.title') }}
      </h1>
      <div class="actions">
        <button
          type="button"
          class="primary"
          @click="creating = true"
        >
          {{ t('redirects.list.new') }}
        </button>
        <button
          type="button"
          @click="importing = true"
        >
          {{ t('redirects.list.import') }}
        </button>
        <a
          class="button"
          :href="redirectsApi.exportUrl(siteId)"
          download="redirects.csv"
        >{{ t('redirects.list.export') }}</a>
      </div>
    </header>

    <form
      class="filters"
      role="search"
      @submit.prevent="page = 1; load()"
    >
      <label for="redirects-search">{{ t('redirects.list.search') }}</label>
      <input
        id="redirects-search"
        v-model="search"
        type="search"
      >
      <label for="redirects-state">{{ t('redirects.list.state') }}</label>
      <select
        id="redirects-state"
        v-model="state"
      >
        <option value="">
          {{ t('redirects.list.allStates') }}
        </option>
        <option
          v-for="option in ['active', 'off', 'hidden', 'broken']"
          :key="option"
          :value="option"
        >
          {{ t(`redirects.states.${option}`) }}
        </option>
      </select>
      <label for="redirects-sort">{{ t('redirects.list.sort') }}</label>
      <select
        id="redirects-sort"
        v-model="sort"
      >
        <option value="source">
          {{ t('redirects.sort.source') }}
        </option>
        <option value="hits">
          {{ t('redirects.sort.hits') }}
        </option>
        <option value="lastUsed">
          {{ t('redirects.sort.lastUsed') }}
        </option>
        <option value="updated">
          {{ t('redirects.sort.updated') }}
        </option>
      </select>
      <button type="submit">
        {{ t('redirects.list.apply') }}
      </button>
    </form>

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-else-if="loading && !list"
      role="status"
    >
      {{ t('redirects.list.loading') }}
    </p>

    <template v-if="list">
      <p role="status">
        {{ t('redirects.list.count', { total: list.total, limit: list.limit }) }}
      </p>
      <table class="sites-table redirects-table">
        <caption class="visually-hidden">
          {{ t('redirects.title') }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('redirects.list.source') }}
            </th>
            <th scope="col">
              {{ t('redirects.list.target') }}
            </th>
            <th scope="col">
              {{ t('redirects.list.kind') }}
            </th>
            <th scope="col">
              {{ t('redirects.list.state') }}
            </th>
            <th scope="col">
              {{ t('redirects.list.hits') }}
            </th>
            <th scope="col">
              {{ t('redirects.list.lastUsed') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="redirect in list.items"
            :key="redirect.id"
            :data-state="redirect.state"
          >
            <th scope="row">
              <button
                type="button"
                class="link"
                @click="open(redirect)"
              >
                <code>{{ redirect.source }}</code><span v-if="redirect.match === 'prefix'"> {{ t('redirects.list.andBelow') }}</span>
              </button>
            </th>
            <td><code>{{ describeTarget(redirect) }}</code></td>
            <td>{{ redirect.statusCode }}</td>
            <td>{{ t(`redirects.states.${redirect.state}`) }}</td>
            <td>{{ redirect.hits }}</td>
            <td>{{ describeLastUsed(redirect) }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="list.items.length === 0">
        {{ t('redirects.list.empty') }}
      </p>

      <nav
        v-if="pageCount > 1"
        class="pager"
        :aria-label="t('redirects.list.pages')"
      >
        <button
          type="button"
          :disabled="page <= 1"
          @click="page--; load()"
        >
          {{ t('redirects.list.previous') }}
        </button>
        <span>{{ t('redirects.list.pageOf', { page, count: pageCount }) }}</span>
        <button
          type="button"
          :disabled="page >= pageCount"
          @click="page++; load()"
        >
          {{ t('redirects.list.next') }}
        </button>
      </nav>
    </template>

    <RedirectDialog
      v-if="creating || editing"
      :site-id="siteId"
      :redirect="editing"
      :pages="pages"
      @saved="afterChange"
      @deleted="afterChange"
      @cancel="closeDialogs"
    />
    <RedirectImportDialog
      v-if="importing"
      :site-id="siteId"
      @imported="load"
      @close="importing = false"
    />
  </section>
</template>
