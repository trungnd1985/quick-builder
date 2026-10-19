<script setup lang="ts">
// A page's or component's publishing actions (012 FR-039, research PR-20), used by the page
// tree, the component list and the editor's top bar, so every place offers the same actions.
// The server checks every action; `canPublish` only decides what is offered.
import { computed, inject, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { routerKey } from 'vue-router'
import { pagesApi } from '../../api/pages'
import { publishingApi, type Publication, type SubjectKind } from '../../api/publishing'
import { ApiError } from '../../api/request'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import RedirectToPagePicker, { type PickablePage } from '../../components/RedirectToPagePicker.vue'
import PublicationBadge from './PublicationBadge.vue'
import ScheduleDialog from './ScheduleDialog.vue'
import MenuReferencesNote from '../menus/MenuReferencesNote.vue'
import { publishingErrorMessage } from './errorMessages'
import { currentDraftVersion, currentPublication, historyRoute as routeToHistory, historyRouteName } from './subjects'

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

const emit = defineEmits<{
  changed: [publication: Publication]
  /** 035: publishing was refused because of these places in the document (JSON pointers). */
  refused: [pointers: string[]]
}>()

const { t, te, d } = useI18n()

const busy = ref(false)
const error = ref<string | null>(null)
const status = ref('')
const confirmingUnpublish = ref(false)
// 017 FR-032: where an unpublished page's address goes; none unless chosen.
const redirectToPageId = ref<string | null>(null)
const sitePages = ref<PickablePage[]>([])

async function askUnpublish(): Promise<void> {
  redirectToPageId.value = null
  if (props.kind === 'page' && sitePages.value.length === 0) {
    try {
      sitePages.value = (await pagesApi.tree(props.siteId)).pages ?? []
    } catch {
      sitePages.value = []
    }
  }
  confirmingUnpublish.value = true
}
const confirmingPublish = ref(false)
const scheduling = ref(false)

/** FR-019: a manual publish cancels the pending scheduled one, so it says so first. */
const pendingPublish = computed(() => props.publication?.pendingSchedules.find((s) => s.action === 'publish') ?? null)
const hasPending = computed(() => (props.publication?.pendingSchedules.length ?? 0) > 0)

const isPublished = computed(() => props.publication?.state === 'published')

// Offered where the app routes to it; the shell always does.
const router = inject(routerKey, null)
const hasHistory = computed(() => router?.hasRoute(historyRouteName(props.kind)) === true)

const historyRoute = computed(() => routeToHistory(props.kind, props.siteId, props.subjectId))

async function draftVersion(): Promise<number | null> {
  if (props.beforePublish) return props.beforePublish()
  return currentDraftVersion(props.kind, props.siteId, props.subjectId)
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
    const problems = caught instanceof ApiError && Array.isArray((caught.problem as unknown as Record<string, unknown> | undefined)?.problems)
      ? ((caught.problem as unknown as { problems: { pointer?: unknown }[] }).problems)
      : []
    const pointers = problems.map((p) => p.pointer).filter((p): p is string => typeof p === 'string')
    if (pointers.length > 0) emit('refused', pointers)
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
    () => publishingApi.unpublish(props.siteId, props.kind, props.subjectId, props.kind === 'page' ? redirectToPageId.value : null),
    () => t('publishing.menu.unpublished'),
  )
  confirmingUnpublish.value = false
}

/** After a schedule is set, changed or cancelled: the fresh publication, schedules and all. */
async function scheduled(): Promise<void> {
  scheduling.value = false
  await run(() => currentPublication(props.kind, props.siteId, props.subjectId), () => t('publishing.schedule.updated'))
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
      @click="askUnpublish"
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
    >
      <!-- 024 FR-026: the menus that link here lose that entry while it is unpublished. -->
      <MenuReferencesNote
        v-if="kind === 'page'"
        :site-id="siteId"
        :target="{ pageId: subjectId }"
        action="unpublish"
      />
      <RedirectToPagePicker
        v-if="kind === 'page'"
        id="publish-unpublish-redirect"
        v-model="redirectToPageId"
        :pages="sitePages"
        :except-page-id="subjectId"
      />
    </ConfirmDialog>

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
