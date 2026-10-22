<script setup lang="ts">
// The submission list's filters (018 FR-027): form, date range, read state and search words.
// Changes apply when the form is sent, so a search is not run on every key press.
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { MAX_SEARCH_LENGTH, type FormSummary, type ReadFilter, type SubmissionFilters } from '../../api/forms'

const props = defineProps<{ filters: SubmissionFilters; forms: FormSummary[] }>()
const emit = defineEmits<{ apply: [filters: SubmissionFilters] }>()
const { t } = useI18n()

const formId = ref('')
const from = ref('')
const to = ref('')
const read = ref<ReadFilter>('all')
const q = ref('')

// The date inputs hold local calendar days; the server takes instants.
const dayOf = (iso?: string) => (iso ? toLocalDay(new Date(iso)) : '')
const startOf = (day: string) => (day ? new Date(`${day}T00:00:00`).toISOString() : undefined)
const endOf = (day: string) => (day ? new Date(`${day}T23:59:59.999`).toISOString() : undefined)

function toLocalDay(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

watch(
  () => props.filters,
  (filters) => {
    formId.value = filters.formId ?? ''
    from.value = dayOf(filters.from)
    to.value = dayOf(filters.to)
    read.value = filters.read ?? 'all'
    q.value = filters.q ?? ''
  },
  { immediate: true },
)

function apply(): void {
  emit('apply', {
    formId: formId.value || undefined,
    from: startOf(from.value),
    to: endOf(to.value),
    read: read.value,
    q: q.value.trim() || undefined,
  })
}

function clear(): void {
  emit('apply', {})
}
</script>

<template>
  <form
    class="submission-filters"
    role="search"
    @submit.prevent="apply"
  >
    <fieldset>
      <legend class="visually-hidden">
        {{ t('forms.filters.legend') }}
      </legend>
      <div class="field">
        <label for="submission-filter-search">{{ t('forms.filters.search') }}</label>
        <input
          id="submission-filter-search"
          v-model="q"
          type="search"
          :maxlength="MAX_SEARCH_LENGTH"
          aria-describedby="submission-filter-search-help"
        >
        <p
          id="submission-filter-search-help"
          class="help"
        >
          {{ t('forms.filters.searchHelp') }}
        </p>
      </div>
      <div class="field">
        <label for="submission-filter-form">{{ t('forms.filters.form') }}</label>
        <select
          id="submission-filter-form"
          v-model="formId"
        >
          <option value="">
            {{ t('forms.filters.allForms') }}
          </option>
          <option
            v-for="form in forms"
            :key="form.formId"
            :value="form.formId"
          >
            {{ form.published ? form.name : `${form.name} (${t('forms.submissions.notPublished')})` }}
          </option>
        </select>
      </div>
      <div class="field">
        <label for="submission-filter-from">{{ t('forms.filters.from') }}</label>
        <input
          id="submission-filter-from"
          v-model="from"
          type="date"
        >
      </div>
      <div class="field">
        <label for="submission-filter-to">{{ t('forms.filters.to') }}</label>
        <input
          id="submission-filter-to"
          v-model="to"
          type="date"
        >
      </div>
      <div class="field">
        <label for="submission-filter-read">{{ t('forms.filters.read') }}</label>
        <select
          id="submission-filter-read"
          v-model="read"
        >
          <option value="all">
            {{ t('forms.filters.readAll') }}
          </option>
          <option value="unread">
            {{ t('forms.filters.readUnread') }}
          </option>
          <option value="read">
            {{ t('forms.filters.readRead') }}
          </option>
        </select>
      </div>
      <div class="actions">
        <button
          type="submit"
          class="primary"
        >
          {{ t('forms.filters.apply') }}
        </button>
        <button
          type="button"
          @click="clear"
        >
          {{ t('forms.filters.clear') }}
        </button>
      </div>
    </fieldset>
  </form>
</template>

<style scoped>
.submission-filters fieldset {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem 1rem;
  align-items: flex-end;
  border: 0;
  padding: 0;
  margin: 0 0 1rem;
}

.submission-filters .field {
  margin: 0;
}

.submission-filters .actions {
  display: flex;
  gap: 0.5rem;
}
</style>
