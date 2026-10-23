<script setup lang="ts">
// The site's submissions (018 US3, FR-025 to FR-030; research FS-21): newest first, filtered and
// searched on the server, paged with its cursor, exported as CSV with the same filters, and
// deleted in a selection by those allowed to. The filters live in the address, so going back
// from a submission returns to the same list.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter, type LocationQuery } from 'vue-router'
import { formsApi, type FormSummary, type SpamCounter, type SubmissionFilters as Filters, type SubmissionSummary } from '../../api/forms'
import { useSitesStore } from '../../app/sitesStore'
import SubmissionFilters from './SubmissionFilters.vue'
import { formsErrorMessage } from './formsErrors'

const { t, te, d } = useI18n()
const route = useRoute()
const router = useRouter()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const FILTER_KEYS = ['formId', 'from', 'to', 'read', 'q'] as const

function filtersOf(query: LocationQuery): Filters {
  const filters: Record<string, string> = {}
  for (const key of FILTER_KEYS) {
    const value = query[key]
    if (typeof value === 'string' && value) filters[key] = value
  }
  return filters as Filters
}

const filters = computed(() => filtersOf(route.query))
const filtered = computed(() => Object.keys(filters.value).length > 0)

const forms = ref<FormSummary[]>([])
// FR-019: what spam protection held back per form; only forms with something to show.
const counters = ref<SpamCounter[]>([])
const items = ref<SubmissionSummary[]>([])
const next = ref<string | null>(null)
const canDelete = ref(false)
const selected = ref<Set<string>>(new Set())
const loading = ref(false)
const error = ref<string | null>(null)
const status = ref('')

async function load(more = false): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const page = await formsApi.list(siteId.value, filters.value, more ? next.value : null)
    items.value = more ? [...items.value, ...page.items] : page.items
    next.value = page.next
    canDelete.value = page.permissions.delete
    if (!more) selected.value = new Set()
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

async function loadForms(): Promise<void> {
  const [known, held] = await Promise.all([
    formsApi.forms(siteId.value).catch(() => ({ items: [] })),
    formsApi.spamCounters(siteId.value).catch(() => ({ items: [] })),
  ])
  forms.value = known.items
  counters.value = held.items.filter((c) => c.trappedKept + c.trappedDropped + c.rateLimited > 0)
}

const formName = (formId: string) => forms.value.find((f) => f.formId === formId)?.name ?? formId

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  await Promise.all([loadForms(), load()])
})

watch(siteId, () => Promise.all([loadForms(), load()]))
watch(filters, () => load(), { deep: true })

function apply(next: Filters): void {
  const query: Record<string, string> = {}
  for (const key of FILTER_KEYS) {
    const value = next[key]
    if (value && !(key === 'read' && value === 'all')) query[key] = value
  }
  void router.replace({ query })
}

const exportUrl = computed(() => formsApi.exportUrl(siteId.value, filters.value))

const allSelected = computed(() => items.value.length > 0 && items.value.every((i) => selected.value.has(i.id)))

function toggle(id: string): void {
  const next = new Set(selected.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  selected.value = next
}

function toggleAll(): void {
  selected.value = allSelected.value ? new Set() : new Set(items.value.map((i) => i.id))
}

async function removeSelected(): Promise<void> {
  const ids = [...selected.value]
  if (ids.length === 0 || !window.confirm(t('forms.submissions.deleteConfirm', { count: ids.length }))) return
  try {
    const result = await formsApi.remove(siteId.value, ids)
    status.value = t('forms.submissions.deleted', { count: result.deleted })
    await load()
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  }
}

const when = (item: SubmissionSummary) => d(new Date(item.receivedAt), 'short')

function retention(form: FormSummary): string {
  return form.retentionDays ? t('forms.submissions.keptFor', { days: form.retentionDays }) : t('forms.submissions.keptUntilDeleted')
}
</script>

<template>
  <section aria-labelledby="submissions-heading">
    <p>
      <RouterLink :to="{ name: 'site', params: { siteId } }">
        {{ t('components.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="submissions-heading">
          {{ t('forms.submissions.title') }}
        </h1>
        <p class="subtitle">
          {{ t('forms.submissions.intro', { site: siteName }) }}
        </p>
      </div>
      <div class="actions">
        <RouterLink
          class="button-link"
          :to="{ name: 'site-spam', params: { siteId } }"
        >
          {{ t('forms.submissions.spamLink') }}
        </RouterLink>
        <a
          class="button-link"
          :href="exportUrl"
          download
          aria-describedby="submissions-export-help"
        >{{ t('forms.submissions.export') }}</a>
        <p
          id="submissions-export-help"
          class="visually-hidden"
        >
          {{ t('forms.submissions.exportHelp') }}
        </p>
      </div>
    </header>

    <SubmissionFilters
      :filters="filters"
      :forms="forms"
      @apply="apply"
    />

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      class="visually-hidden"
      role="status"
    >
      {{ status }}
    </p>

    <p
      v-if="!loading && !error && items.length === 0"
      class="card"
    >
      {{ filtered ? t('forms.submissions.emptyFiltered') : t('forms.submissions.empty') }}
    </p>

    <div
      v-if="canDelete && items.length > 0"
      class="selection-bar"
    >
      <span>{{ t('forms.submissions.selected', { count: selected.size }) }}</span>
      <button
        type="button"
        class="danger"
        :disabled="selected.size === 0"
        @click="removeSelected"
      >
        {{ t('forms.submissions.delete') }}
      </button>
    </div>

    <table
      v-if="items.length > 0"
      class="sites-table submissions-table"
    >
      <caption class="visually-hidden">
        {{ t('forms.submissions.title') }}
      </caption>
      <thead>
        <tr>
          <th
            v-if="canDelete"
            scope="col"
          >
            <input
              type="checkbox"
              :checked="allSelected"
              :aria-label="t('forms.submissions.selectAll')"
              @change="toggleAll"
            >
          </th>
          <th scope="col">
            {{ t('forms.submissions.columns.received') }}
          </th>
          <th scope="col">
            {{ t('forms.submissions.columns.form') }}
          </th>
          <th scope="col">
            {{ t('forms.submissions.columns.page') }}
          </th>
          <th scope="col">
            {{ t('forms.submissions.columns.preview') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="item in items"
          :key="item.id"
          :class="{ unread: !item.read }"
        >
          <td v-if="canDelete">
            <input
              type="checkbox"
              :checked="selected.has(item.id)"
              :aria-label="t('forms.submissions.select', { when: when(item) })"
              @change="toggle(item.id)"
            >
          </td>
          <th scope="row">
            <RouterLink :to="{ name: 'site-submission', params: { siteId, submissionId: item.id }, query: route.query }">
              {{ when(item) }}
            </RouterLink>
            <span
              v-if="!item.read"
              class="badge"
            >{{ t('forms.submissions.unread') }}</span>
          </th>
          <td>
            {{ item.formName }}
            <span
              v-if="!item.formPublished"
              class="badge"
            >{{ t('forms.submissions.notPublished') }}</span>
          </td>
          <td><code>{{ item.pagePath }}</code></td>
          <td class="preview">
            {{ item.preview }}
          </td>
        </tr>
      </tbody>
    </table>

    <button
      v-if="next"
      type="button"
      :disabled="loading"
      @click="load(true)"
    >
      {{ t('forms.submissions.more') }}
    </button>

    <section
      v-if="counters.length > 0"
      aria-labelledby="spam-counters-heading"
      class="submission-forms"
    >
      <h2 id="spam-counters-heading">
        {{ t('forms.counters.heading') }}
      </h2>
      <table class="sites-table">
        <thead>
          <tr>
            <th scope="col">
              {{ t('forms.counters.form') }}
            </th>
            <th scope="col">
              {{ t('forms.counters.trappedKept') }}
            </th>
            <th scope="col">
              {{ t('forms.counters.trappedDropped') }}
            </th>
            <th scope="col">
              {{ t('forms.counters.rateLimited') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="counter in counters"
            :key="counter.formId"
          >
            <th scope="row">
              {{ formName(counter.formId) }}
            </th>
            <td>{{ counter.trappedKept }}</td>
            <td>{{ counter.trappedDropped }}</td>
            <td>{{ counter.rateLimited }}</td>
          </tr>
        </tbody>
      </table>
    </section>

    <section
      v-if="forms.length > 0"
      aria-labelledby="submission-forms-heading"
      class="submission-forms"
    >
      <h2 id="submission-forms-heading">
        {{ t('forms.submissions.formsHeading') }}
      </h2>
      <ul>
        <li
          v-for="form in forms"
          :key="form.formId"
        >
          {{ form.name }}
          <span
            v-if="!form.published"
            class="badge"
          >{{ t('forms.submissions.notPublished') }}</span>
          — {{ retention(form) }}
        </li>
      </ul>
    </section>
  </section>
</template>

<style scoped>
.submissions-table tr.unread th,
.submissions-table tr.unread td {
  font-weight: 600;
}

.submissions-table .preview {
  max-width: 28rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.selection-bar {
  display: flex;
  gap: 1rem;
  align-items: center;
  margin-bottom: 0.5rem;
}

.submission-forms {
  margin-top: 2rem;
}
</style>
