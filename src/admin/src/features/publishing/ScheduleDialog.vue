<script setup lang="ts">
// Scheduling a publish or an unpublish (012 FR-018 to FR-021, research PR-14). The member
// picks a date and time in their own time zone, named on screen; the server gets UTC. A
// scheduled publish captures the draft as it is now, so what goes live is what was reviewed.
// An existing schedule is shown, and can be moved or cancelled.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { publishingApi, type Publication, type ScheduleAction, type SubjectKind } from '../../api/publishing'
import ZonedDateTimeInput from '../../components/ZonedDateTimeInput.vue'
import { publishingErrorMessage } from './errorMessages'

const props = defineProps<{
  dialogId: string
  siteId: string
  kind: SubjectKind
  subjectId: string
  title: string
  publication: Publication | null | undefined
  /** The draft version to capture for a publish; saves pending changes first in the editor. */
  draftVersion: () => Promise<number | null>
}>()

const emit = defineEmits<{ changed: []; close: [] }>()

const { t, te, d } = useI18n()

/** Components have no unpublish schedule, and only something live can be taken down. */
const actions = computed<ScheduleAction[]>(() =>
  props.kind === 'page' && props.publication?.state === 'published' ? ['publish', 'unpublish'] : ['publish'])

const action = ref<ScheduleAction>('publish')
const existing = computed(() => props.publication?.pendingSchedules.find((s) => s.action === action.value) ?? null)

/** The chosen moment in UTC, or null while incomplete; it starts an hour ahead, or at the existing schedule. */
const due = ref<string | null>(existing.value ? existing.value.dueAtUtc : new Date(Date.now() + 60 * 60 * 1000).toISOString())
const busy = ref(false)
const error = ref<string | null>(null)
const form = ref<HTMLFormElement | null>(null)

onMounted(async () => {
  await nextTick()
  form.value?.querySelector<HTMLElement>('select, input')?.focus()
})

async function save(): Promise<void> {
  error.value = null
  if (!due.value || new Date(due.value).getTime() <= Date.now()) {
    error.value = t('publishing.schedule.inPast')
    return
  }

  busy.value = true
  try {
    const version = action.value === 'publish' ? await props.draftVersion() : undefined
    if (version === null) throw new Error('The draft could not be saved.')
    await publishingApi.schedule(props.siteId, props.kind, props.subjectId, action.value, due.value, version)
    emit('changed')
  } catch (caught) {
    error.value = publishingErrorMessage(t, te, caught)
  } finally {
    busy.value = false
  }
}

async function cancelSchedule(): Promise<void> {
  busy.value = true
  error.value = null
  try {
    await publishingApi.cancelSchedule(props.siteId, props.kind, props.subjectId, action.value)
    emit('changed')
  } catch (caught) {
    error.value = publishingErrorMessage(t, te, caught)
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('close')"
  >
    <form
      ref="form"
      class="card dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-title`"
      novalidate
      @keydown="onKeydown"
      @submit.prevent="save"
    >
      <h2 :id="`${dialogId}-title`">
        {{ t('publishing.schedule.title') }}: {{ title }}
      </h2>

      <div
        v-if="actions.length > 1"
        class="field"
      >
        <label :for="`${dialogId}-action`">{{ t('publishing.schedule.action') }}</label>
        <select
          :id="`${dialogId}-action`"
          v-model="action"
        >
          <option
            v-for="option in actions"
            :key="option"
            :value="option"
          >
            {{ t(`publishing.schedule.${option}`) }}
          </option>
        </select>
      </div>

      <p v-if="existing">
        {{ t('publishing.schedule.existing', { when: d(new Date(existing.dueAtUtc), 'short') }) }}
      </p>

      <ZonedDateTimeInput
        :id="dialogId"
        v-model="due"
      />

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>

      <div class="actions">
        <button
          v-if="existing"
          type="button"
          class="danger"
          :disabled="busy"
          @click="cancelSchedule"
        >
          {{ t('publishing.menu.cancelSchedule') }}
        </button>
        <button
          type="button"
          :disabled="busy"
          @click="emit('close')"
        >
          {{ t('publishing.confirm.cancel') }}
        </button>
        <button
          type="submit"
          class="primary"
          :disabled="busy"
        >
          {{ existing ? t('publishing.menu.changeSchedule') : t('publishing.schedule.save') }}
        </button>
      </div>
    </form>
  </div>
</template>
