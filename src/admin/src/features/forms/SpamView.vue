<script setup lang="ts">
// The site's Spam list (018 FR-016a, clarification 5): what the trap field caught in the last 30
// days, newest first. Any site role can restore a submission a real visitor sent, which moves it
// to the inbox unread and sends its notification; Designers and Administrators can delete.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { formsApi, type SubmissionSummary } from '../../api/forms'
import { useSitesStore } from '../../app/sitesStore'
import { formsErrorMessage } from './formsErrors'

const { t, te, d } = useI18n()
const route = useRoute()
const sites = useSitesStore()

const siteId = computed(() => String(route.params.siteId))
const siteName = computed(() => sites.sites.find((s) => s.id === siteId.value)?.name ?? '')

const items = ref<SubmissionSummary[]>([])
const next = ref<string | null>(null)
const canDelete = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const status = ref('')

async function load(more = false): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const page = await formsApi.list(siteId.value, { box: 'spam' }, more ? next.value : null)
    items.value = more ? [...items.value, ...page.items] : page.items
    next.value = page.next
    canDelete.value = page.permissions.delete
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  if (sites.sites.length === 0) await sites.load().catch(() => undefined)
  await load()
})

watch(siteId, () => load())

const when = (item: SubmissionSummary) => d(new Date(item.receivedAt), 'short')

async function restore(item: SubmissionSummary): Promise<void> {
  try {
    await formsApi.restore(siteId.value, item.id)
    items.value = items.value.filter((i) => i.id !== item.id)
    status.value = t('forms.spam.restored')
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  }
}

async function remove(item: SubmissionSummary): Promise<void> {
  if (!window.confirm(t('forms.spam.deleteConfirm'))) return
  try {
    await formsApi.remove(siteId.value, [item.id])
    items.value = items.value.filter((i) => i.id !== item.id)
    status.value = t('forms.spam.deleted')
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  }
}
</script>

<template>
  <section aria-labelledby="spam-heading">
    <p>
      <RouterLink :to="{ name: 'site-submissions', params: { siteId } }">
        {{ t('forms.spam.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="spam-heading">
          {{ t('forms.spam.title') }}
        </h1>
        <p class="subtitle">
          {{ t('forms.spam.intro', { site: siteName }) }}
        </p>
      </div>
    </header>

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
      {{ t('forms.spam.empty') }}
    </p>

    <table
      v-if="items.length > 0"
      class="sites-table"
    >
      <caption class="visually-hidden">
        {{ t('forms.spam.title') }}
      </caption>
      <thead>
        <tr>
          <th scope="col">
            {{ t('forms.submissions.columns.received') }}
          </th>
          <th scope="col">
            {{ t('forms.submissions.columns.form') }}
          </th>
          <th scope="col">
            {{ t('forms.submissions.columns.preview') }}
          </th>
          <th scope="col">
            <span class="visually-hidden">{{ t('forms.submissions.columns.status') }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="item in items"
          :key="item.id"
        >
          <th scope="row">
            {{ when(item) }}
          </th>
          <td>{{ item.formName }}</td>
          <td>{{ item.preview }}</td>
          <td class="actions">
            <button
              type="button"
              :aria-label="t('forms.spam.restoreLabel', { when: when(item) })"
              @click="restore(item)"
            >
              {{ t('forms.spam.restore') }}
            </button>
            <button
              v-if="canDelete"
              type="button"
              class="danger"
              :aria-label="t('forms.spam.deleteLabel', { when: when(item) })"
              @click="remove(item)"
            >
              {{ t('forms.spam.delete') }}
            </button>
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
      {{ t('forms.spam.more') }}
    </button>
  </section>
</template>
