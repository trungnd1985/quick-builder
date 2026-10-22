<script setup lang="ts">
// One submission (018 FR-026, FR-028): every value under its field's label as the form had it,
// where it was sent from, whether its email went out, and how long it is kept. Opening it marks
// it read; it can be marked unread again, and deleted by those allowed to.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { formsApi, type Submission, type SubmissionValue } from '../../api/forms'
import { formsErrorMessage } from './formsErrors'

const { t, te, d } = useI18n()
const route = useRoute()
const router = useRouter()

const siteId = computed(() => String(route.params.siteId))
const submissionId = computed(() => String(route.params.submissionId))
// The list's filters, carried here so "back" returns to the same list.
const listRoute = computed(() => ({ name: 'site-submissions', params: { siteId: siteId.value }, query: route.query }))

const submission = ref<Submission | null>(null)
const canDelete = ref(false)
const loading = ref(true)
const error = ref<string | null>(null)
const status = ref('')

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    submission.value = await formsApi.get(siteId.value, submissionId.value)
  } catch (caught) {
    submission.value = null
    error.value = formsErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

// Whether this user may delete: the list reports it; one unfiltered row is enough to ask.
async function loadPermissions(): Promise<void> {
  canDelete.value = (await formsApi.list(siteId.value).catch(() => null))?.permissions.delete ?? false
}

onMounted(() => Promise.all([load(), loadPermissions()]))
watch(submissionId, () => load())

const text = (value: SubmissionValue) => (Array.isArray(value.value) ? value.value.join(', ') : value.value)

async function toggleRead(): Promise<void> {
  if (!submission.value) return
  const read = !submission.value.read
  try {
    await formsApi.mark(siteId.value, submission.value.id, read)
    submission.value = { ...submission.value, read }
    status.value = read ? '' : t('forms.detail.markedUnread')
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  }
}

async function remove(): Promise<void> {
  if (!submission.value || !window.confirm(t('forms.detail.deleteConfirm'))) return
  try {
    await formsApi.remove(siteId.value, [submission.value.id])
    await router.push(listRoute.value)
  } catch (caught) {
    error.value = formsErrorMessage(t, te, caught)
  }
}
</script>

<template>
  <section aria-labelledby="submission-heading">
    <p>
      <RouterLink :to="listRoute">
        {{ t('forms.detail.back') }}
      </RouterLink>
    </p>

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

    <template v-if="submission">
      <header class="page-header">
        <div>
          <h1 id="submission-heading">
            {{ t('forms.detail.title', { form: submission.formName }) }}
            <span
              v-if="!submission.formPublished"
              class="badge"
            >{{ t('forms.submissions.notPublished') }}</span>
          </h1>
        </div>
        <div class="actions">
          <button
            type="button"
            @click="toggleRead"
          >
            {{ submission.read ? t('forms.detail.markUnread') : t('forms.detail.markRead') }}
          </button>
          <button
            v-if="canDelete"
            type="button"
            class="danger"
            @click="remove"
          >
            {{ t('forms.detail.delete') }}
          </button>
        </div>
      </header>

      <p
        v-if="submission.spam"
        class="card notice"
        role="note"
      >
        {{ t('forms.detail.spam') }}
      </p>

      <dl class="submission-facts">
        <dt>{{ t('forms.detail.received') }}</dt>
        <dd>{{ d(new Date(submission.receivedAt), 'short') }}</dd>
        <dt>{{ t('forms.detail.page') }}</dt>
        <dd><code>{{ submission.pagePath }}</code></dd>
        <dt>{{ t('forms.detail.notification') }}</dt>
        <dd>
          {{ t(`forms.notification.${submission.notification}`) }}
          <template v-if="submission.notificationError">
            <br><span class="help">{{ submission.notificationError }}</span>
          </template>
        </dd>
        <dt>{{ t('forms.detail.retention') }}</dt>
        <dd>
          {{ submission.retentionDays
            ? t('forms.submissions.keptFor', { days: submission.retentionDays })
            : t('forms.submissions.keptUntilDeleted') }}
        </dd>
      </dl>

      <h2>{{ t('forms.detail.values') }}</h2>
      <dl class="submission-values">
        <template
          v-for="value in submission.values"
          :key="value.fieldId"
        >
          <dt>{{ value.label }}</dt>
          <dd>{{ text(value) || t('forms.detail.noValue') }}</dd>
        </template>
      </dl>
    </template>
  </section>
</template>

<style scoped>
.submission-facts,
.submission-values {
  display: grid;
  grid-template-columns: minmax(8rem, max-content) 1fr;
  gap: 0.5rem 1.5rem;
}

.submission-facts dt,
.submission-values dt {
  font-weight: 600;
}

.submission-facts dd,
.submission-values dd {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
