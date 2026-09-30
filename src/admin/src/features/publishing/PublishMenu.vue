<script setup lang="ts">
// A page's or component's publishing actions (012 FR-039, research PR-20), used by the page
// tree, the component list and the editor's top bar, so every place offers the same actions.
// The server checks every action; `canPublish` only decides what is offered.
import { computed, inject, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { routerKey } from 'vue-router'
import { pagesApi } from '../../api/pages'
import { publishingApi, type Publication, type SubjectKind } from '../../api/publishing'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import PublicationBadge from './PublicationBadge.vue'
import ScheduleDialog from './ScheduleDialog.vue'
import { publishingErrorMessage } from './errorMessages'

const props = defineProps<{
  siteId: string
  kind: SubjectKind
  subjectId: string
  title: string
  publication: Publication | null | undefined
  canPublish: boolean
  /**
   * Called before publishing, to save pending changes first (the editor). Returns the draft
   * version to publish; without it, the current saved draft is published.
   */
  beforePublish?: () => Promise<number | null>
}>()

const emit = defineEmits<{ changed: [publication: Publication] }>()

const { t, te, d } = useI18n()

const busy = ref(false)
const error = ref<string | null>(null)
const status = ref('')
const confirmingUnpublish = ref(false)
const confirmingPublish = ref(false)
const scheduling = ref(false)

/** FR-019: a manual publish cancels the pending scheduled one, so it says so first. */
const pendingPublish = computed(() => props.publication?.pendingSchedules.find((s) => s.action === 'publish') ?? null)
const hasPending = computed(() => (props.publication?.pendingSchedules.length ?? 0) > 0)

const isPublished = computed(() => props.publication?.state === 'published')

// Offered where the app routes to it; the shell always does.
const router = inject(routerKey, null)
const hasHistory = computed(() => router?.hasRoute(props.kind === 'page' ? 'page-history' : 'component-history') === true)

const historyRoute = computed(() =>
  props.kind === 'page'
    ? { name: 'page-history', params: { siteId: props.siteId, pageId: props.subjectId } }
    : { name: 'component-history', params: { siteId: props.siteId, componentId: props.subjectId } })

async function draftVersion(): Promise<number | null> {
  if (props.beforePublish) return props.beforePublish()
  const draft = props.kind === 'page'
    ? await pagesApi.pageDraft(props.siteId, props.subjectId)
    : await pagesApi.componentDraft(props.siteId, props.subjectId)
  return draft.version
}

async function run(action: () => Promise<Publication>, message: (p: Publication) => string): Promise<void> {
  busy.value = true
  error.value = null
  status.value = ''
  try {
    const publication = await action()
    status.value = message(publication)
    emit('changed', publication)
  } catch (caught) {
    error.value = publishingErrorMessage(t, te, caught)
  } finally {
    busy.value = false
  }
}

function askPublish(): void {
  if (pendingPublish.value) confirmingPublish.value = true
  else void publish()
}

async function publish(): Promise<void> {
  confirmingPublish.value = false
  await run(async () => {
    status.value = props.beforePublish ? t('publishing.menu.savingFirst') : ''
    const version = await draftVersion()
    if (version === null) throw new Error('The draft could not be saved.')
    return publishingApi.publish(props.siteId, props.kind, props.subjectId, version)
  }, (p) => t('publishing.menu.published', { number: p.liveNumber ?? 0 }))
}

/** Taking something down asks first: visitors lose it from the next request (FR-005). */
async function unpublish(): Promise<void> {
  await run(
    () => publishingApi.unpublish(props.siteId, props.kind, props.subjectId),
    () => t('publishing.menu.unpublished'),
  )
  confirmingUnpublish.value = false
}

/** After a schedule is set, changed or cancelled: the fresh publication, schedules and all. */
async function scheduled(): Promise<void> {
  scheduling.value = false
  await run(async () => {
    const subject = props.kind === 'page'
      ? await pagesApi.get(props.siteId, props.subjectId)
      : await pagesApi.getComponent(props.siteId, props.subjectId)
    return subject.publication
  }, () => t('publishing.schedule.updated'))
}
</script>

<template>
  <div
    class="publish-menu"
    role="group"
    :aria-label="t('publishing.menu.label')"
  >
    <PublicationBadge :publication="publication" />

    <button
      v-if="canPublish"
      type="button"
      class="primary"
      :disabled="busy"
      @click="askPublish"
    >
      {{ busy ? t('publishing.menu.publishing') : t('publishing.menu.publish') }}
    </button>

    <button
      v-if="canPublish && isPublished"
      type="button"
      :disabled="busy"
      @click="confirmingUnpublish = true"
    >
      {{ t('publishing.menu.unpublish') }}
    </button>

    <button
      v-if="canPublish"
      type="button"
      :disabled="busy"
      @click="scheduling = true"
    >
      {{ hasPending ? t('publishing.menu.changeSchedule') : t('publishing.menu.schedule') }}
    </button>

    <a
      v-if="isPublished && publication?.liveUrl"
      class="button-link"
      :href="publication.liveUrl"
      target="_blank"
      rel="noopener noreferrer"
    >{{ t('publishing.menu.viewLive') }}</a>

    <RouterLink
      v-if="hasHistory"
      class="button-link"
      :to="historyRoute"
      :aria-label="`${t('publishing.menu.history')}: ${title}`"
    >
      {{ t('publishing.menu.history') }}
    </RouterLink>

    <slot
      :busy="busy"
      :run="run"
    />

    <p
      class="visually-hidden"
      role="status"
      aria-live="polite"
    >
      {{ status }}
    </p>
    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>

    <ConfirmDialog
      v-if="confirmingUnpublish"
      :dialog-id="`unpublish-${subjectId}`"
      :title="t('publishing.menu.unpublish')"
      :body="t('publishing.confirm.unpublish', { title })"
      :confirm-label="t('publishing.menu.unpublish')"
      :cancel-label="t('publishing.confirm.cancel')"
      :busy-label="t('publishing.menu.publishing')"
      :busy="busy"
      danger
      @confirm="unpublish"
      @cancel="confirmingUnpublish = false"
    />

    <ConfirmDialog
      v-if="confirmingPublish && pendingPublish"
      :dialog-id="`publish-`"
      :title="t('publishing.menu.publish')"
      :body="t('publishing.confirm.supersedes', { when: d(new Date(pendingPublish.dueAtUtc), 'short') })"
      :confirm-label="t('publishing.menu.publish')"
      :cancel-label="t('publishing.confirm.cancel')"
      :busy-label="t('publishing.menu.publishing')"
      :busy="busy"
      @confirm="publish"
      @cancel="confirmingPublish = false"
    />

    <ScheduleDialog
      v-if="scheduling"
      :dialog-id="`schedule-`"
      :site-id="siteId"
      :kind="kind"
      :subject-id="subjectId"
      :title="title"
      :publication="publication"
      :draft-version="draftVersion"
      @changed="scheduled"
      @close="scheduling = false"
    />
  </div>
</template>

<style scoped>
.publish-menu {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.publish-menu .error {
  flex-basis: 100%;
}
</style>
