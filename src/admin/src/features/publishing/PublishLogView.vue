<script setup lang="ts">
// The site's publish log, for Administrators (012 FR-017, clarification Q2): every publishing
// action, newest first, including pages and components that have since been deleted. Paged
// with the server's cursor, so an entry written meanwhile never shifts a page still to come.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { publishingApi, type LogEntry, type SubjectKind } from '../../api/publishing'
import { useSitesStore } from '../../app/sitesStore'
import { publishingErrorMessage } from './errorMessages'

type KindFilter = '' | SubjectKind | 'design'

const { t, te, d } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const kind = ref<KindFilter>('')
const entries = ref<LogEntry[]>([])
const nextCursor = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

async function load(more = false): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const page = await publishingApi.log(siteId.value, {
      subjectKind: kind.value || undefined,
      cursor: more ? nextCursor.value ?? undefined : undefined,
    })
    entries.value = more ? [...entries.value, ...page.items] : page.items
    nextCursor.value = page.nextCursor
  } catch (caught) {
    error.value = publishingErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  await load()
})

watch([siteId, kind], () => load())

function subject(entry: LogEntry): string {
  if (entry.subjectKind === 'design') return t('publishing.log.design')
  return entry.subjectPath ? `${entry.subjectLabel} (${entry.subjectPath})` : entry.subjectLabel
}

function historyRoute(entry: LogEntry) {
  return entry.subjectKind === 'page'
    ? { name: 'page-history', params: { siteId: siteId.value, pageId: entry.subjectId } }
    : { name: 'component-history', params: { siteId: siteId.value, componentId: entry.subjectId } }
}

function what(entry: LogEntry): string {
  const action = t(`publishing.actions.${entry.action}`, { number: entry.restoredFromNumber ?? entry.versionNumber ?? 0 })
  return entry.versionNumber !== null && entry.action !== 'rollback'
    ? `${action} — ${t('publishing.history.version', { number: entry.versionNumber })}`
    : action
}

function who(entry: LogEntry): string {
  if (entry.actor) return entry.actor.displayName ?? ''
  if (entry.scheduleOwner) return t('publishing.log.schedule', { who: entry.scheduleOwner.displayName ?? '' })
  return ''
}
</script>

<template>
  <section aria-labelledby="publish-log-heading">
    <p>
      <RouterLink :to="{ name: 'site', params: { siteId } }">
        {{ t('components.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="publish-log-heading">
          {{ t('publishing.log.title') }}
        </h1>
        <p class="subtitle">
          {{ t('publishing.log.intro', { site: siteName }) }}
        </p>
      </div>
    </header>

    <div class="field">
      <label for="publish-log-kind">{{ t('publishing.log.filters.kind') }}</label>
      <select
        id="publish-log-kind"
        v-model="kind"
      >
        <option value="">
          {{ t('publishing.log.filters.all') }}
        </option>
        <option value="page">
          {{ t('publishing.log.filters.page') }}
        </option>
        <option value="component">
          {{ t('publishing.log.filters.component') }}
        </option>
        <option value="design">
          {{ t('publishing.log.filters.design') }}
        </option>
      </select>
    </div>

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>

    <p
      v-if="!loading && !error && entries.length === 0"
      class="card"
    >
      {{ t('publishing.log.empty') }}
    </p>

    <table
      v-if="entries.length > 0"
      class="sites-table"
    >
      <caption class="visually-hidden">
        {{ t('publishing.log.title') }}
      </caption>
      <thead>
        <tr>
          <th scope="col">
            {{ t('publishing.log.columns.when') }}
          </th>
          <th scope="col">
            {{ t('publishing.log.columns.what') }}
          </th>
          <th scope="col">
            {{ t('publishing.log.columns.subject') }}
          </th>
          <th scope="col">
            {{ t('publishing.log.columns.who') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="entry in entries"
          :key="entry.id"
        >
          <th scope="row">
            {{ d(new Date(entry.occurredAtUtc), 'short') }}
          </th>
          <td>{{ what(entry) }}</td>
          <td>
            <RouterLink
              v-if="entry.subjectKind !== 'design' && entry.subjectExists"
              :to="historyRoute(entry)"
            >
              {{ subject(entry) }}
            </RouterLink>
            <template v-else>
              {{ subject(entry) }}
              <span
                v-if="!entry.subjectExists"
                class="badge"
              >{{ t('publishing.log.deleted') }}</span>
            </template>
          </td>
          <td>{{ who(entry) }}</td>
        </tr>
      </tbody>
    </table>

    <button
      v-if="nextCursor"
      type="button"
      :disabled="loading"
      @click="load(true)"
    >
      {{ t('publishing.log.more') }}
    </button>
  </section>
</template>
