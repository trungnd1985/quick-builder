<script setup lang="ts">
// A page's or component's publishing actions (012 FR-039, research PR-20), used by the page
// tree, the component list and the editor's top bar, so every place offers the same actions.
// The server checks every action; `canPublish` only decides what is offered.
import { computed, inject, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { routerKey } from 'vue-router'
import { pagesApi } from '../../api/pages'
import { publishingApi, type Publication, type SubjectKind } from '../../api/publishing'
import PublicationBadge from './PublicationBadge.vue'
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

const { t, te } = useI18n()

const busy = ref(false)
const error = ref<string | null>(null)
const status = ref('')

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

async function publish(): Promise<void> {
  await run(async () => {
    status.value = props.beforePublish ? t('publishing.menu.savingFirst') : ''
    const version = await draftVersion()
    if (version === null) throw new Error('The draft could not be saved.')
    return publishingApi.publish(props.siteId, props.kind, props.subjectId, version)
  }, (p) => t('publishing.menu.published', { number: p.liveNumber ?? 0 }))
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
      @click="publish"
    >
      {{ busy ? t('publishing.menu.publishing') : t('publishing.menu.publish') }}
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
