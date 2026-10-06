<script setup lang="ts">
// 013 US5 (FR-014): where a file is used — pages first, then components — each with what
// uses it: the draft, the live version, or only versions kept for rollback. Places link to
// their editor. Someone who may not see page content gets the counts alone. Titles are data:
// text only.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaApi, type MediaFile, type MediaUsage, type MediaUsagePlace } from '../../api/media'

const props = defineProps<{ siteId: string; file: Pick<MediaFile, 'id' | 'contentVersion' | 'canDelete'> }>()
const emit = defineEmits<{ loaded: [usage: MediaUsage] }>()

const { t } = useI18n()
const usage = ref<MediaUsage | null>(null)
const failed = ref(false)

const pages = computed(() => usage.value?.places.filter((p) => p.kind === 'page') ?? [])
const components = computed(() => usage.value?.places.filter((p) => p.kind === 'component') ?? [])
const summary = computed(() => usage.value?.summary)
const liveRule = computed(() => (summary.value?.inLiveUse ?? false) && !props.file.canDelete)

async function load(): Promise<void> {
  failed.value = false
  try {
    usage.value = await mediaApi.usage(props.siteId, props.file.id)
    emit('loaded', usage.value)
  } catch {
    failed.value = true
  }
}

watch(() => [props.file.id, props.file.contentVersion], () => void load(), { immediate: true })

defineExpose({ reload: load })

function editor(place: MediaUsagePlace) {
  return place.kind === 'page'
    ? { name: 'page-editor', params: { siteId: props.siteId, pageId: place.id } }
    : { name: 'component-editor', params: { siteId: props.siteId, componentId: place.id } }
}
</script>

<template>
  <section
    class="media-usage"
    :aria-labelledby="`media-usage-${file.id}`"
  >
    <h3 :id="`media-usage-${file.id}`">
      {{ t('media.usage.title') }}
    </h3>

    <p
      v-if="failed"
      role="alert"
    >
      {{ t('media.usage.failed') }}
    </p>
    <p
      v-else-if="!summary"
      role="status"
    >
      {{ t('media.usage.loading') }}
    </p>
    <template v-else>
      <p v-if="summary.places === 0">
        {{ t('media.usage.none') }}
      </p>
      <p v-else>
        {{ t('media.usage.counts', { places: summary.places, draft: summary.draftPlaces, live: summary.livePlaces, history: summary.historyOnlyPlaces }) }}
      </p>

      <p
        v-if="liveRule"
        class="notice"
      >
        {{ t('media.usage.liveRule') }}
      </p>

      <template
        v-for="group in [{ key: 'pages', places: pages }, { key: 'components', places: components }]"
        :key="group.key"
      >
        <template v-if="group.places.length > 0">
          <h4>{{ t(`media.usage.${group.key}`) }}</h4>
          <ul class="media-usage__places">
            <li
              v-for="place in group.places"
              :key="place.id"
            >
              <RouterLink :to="editor(place)">
                {{ place.title }}
              </RouterLink>
              <span
                v-if="place.live"
                class="badge badge--live"
              >{{ t('media.usage.badges.live') }}</span>
              <span
                v-if="place.inDraft"
                class="badge"
              >{{ t('media.usage.badges.draft') }}</span>
              <span
                v-if="place.inHistory && !place.live && !place.inDraft"
                class="badge"
              >{{ t('media.usage.badges.history') }}</span>
              <span
                v-if="place.livePagesThroughComponent"
                class="help"
              >{{ t('media.usage.livePages', { n: place.livePagesThroughComponent }, place.livePagesThroughComponent) }}</span>
            </li>
          </ul>
        </template>
      </template>
    </template>
  </section>
</template>
