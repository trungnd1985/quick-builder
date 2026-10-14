<script setup lang="ts">
import { computed, ref, watch, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { sitesApi, type Site } from '../../api/sites'
import MediaControl from '../../editor/panel/controls/MediaControl.vue'
import { isObject, type Json } from '../../editor/schema/subsetValidator'
import { siteErrorMessage } from './siteErrors'

// 017 US1 (FR-005, FR-006, research SE-08): how the site presents itself to search engines
// and link previews. The title suffix follows every page title that has no meta title of its
// own; the default image stands in for pages without a sharing image; the organisation is
// described on the home page. Designers and Administrators change them.

const props = defineProps<{ site: Site }>()
const emit = defineEmits<{ updated: [site: Site] }>()
const { t, te } = useI18n()

const canManage = computed(() => props.site.permissions?.canManageSearch === true)

const titleSuffix = ref('')
const organisationName = ref('')
// Shallow: a recursive JSON type would make the ref unwrap without end.
const defaultImage = shallowRef<Json | undefined>(undefined)
const organisationLogo = shallowRef<Json | undefined>(undefined)
const saving = ref(false)
const error = ref<string | null>(null)
const status = ref('')

watch(
  () => props.site,
  (site) => {
    titleSuffix.value = site.search?.titleSuffix ?? ''
    organisationName.value = site.search?.organisationName ?? ''
    defaultImage.value = site.search?.defaultImageId ? { mediaId: site.search.defaultImageId } : undefined
    organisationLogo.value = site.search?.organisationLogoId ? { mediaId: site.search.organisationLogoId } : undefined
  },
  { immediate: true },
)

const idOf = (value: Json | undefined): string | null =>
  isObject(value) && typeof value.mediaId === 'string' ? value.mediaId : null

async function save(): Promise<void> {
  saving.value = true
  error.value = null
  status.value = ''
  try {
    const saved = await sitesApi.putSearchSettings(props.site.id, {
      titleSuffix: titleSuffix.value === '' ? null : titleSuffix.value,
      defaultImageId: idOf(defaultImage.value),
      organisationName: organisationName.value.trim() === '' ? null : organisationName.value,
      organisationLogoId: idOf(organisationLogo.value),
      version: props.site.version,
    })
    status.value = t('sites.search.saved')
    emit('updated', saved)
  } catch (caught) {
    error.value = siteErrorMessage(t, te, caught)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section
    class="card"
    aria-labelledby="site-search-heading"
  >
    <h2 id="site-search-heading">
      {{ t('sites.search.heading') }}
    </h2>
    <p class="hint">
      {{ t('sites.search.intro') }}
    </p>

    <form
      novalidate
      @submit.prevent="save"
    >
      <div class="field">
        <label for="site-search-suffix">{{ t('sites.search.titleSuffix') }}</label>
        <input
          id="site-search-suffix"
          v-model="titleSuffix"
          maxlength="60"
          :disabled="!canManage"
          aria-describedby="site-search-suffix-help"
        >
        <p
          id="site-search-suffix-help"
          class="hint"
        >
          {{ t('sites.search.titleSuffixHelp') }}
        </p>
      </div>

      <MediaControl
        v-if="canManage"
        id="site-search-default-image"
        :site-id="site.id"
        :value="defaultImage"
        :label="t('sites.search.defaultImage')"
        prop="defaultImage"
        kind="image"
        @change="(value) => (defaultImage = value)"
        @choose="(value) => (defaultImage = value)"
      />

      <div class="field">
        <label for="site-search-organisation">{{ t('sites.search.organisationName') }}</label>
        <input
          id="site-search-organisation"
          v-model="organisationName"
          maxlength="200"
          :disabled="!canManage"
        >
      </div>

      <MediaControl
        v-if="canManage"
        id="site-search-logo"
        :site-id="site.id"
        :value="organisationLogo"
        :label="t('sites.search.organisationLogo')"
        prop="organisationLogo"
        kind="image"
        @change="(value) => (organisationLogo = value)"
        @choose="(value) => (organisationLogo = value)"
      />

      <p
        v-if="!canManage"
        class="hint"
      >
        {{ t('sites.search.images', { image: defaultImage ? t('sites.search.set') : t('sites.search.notSet'), logo: organisationLogo ? t('sites.search.set') : t('sites.search.notSet') }) }}
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

      <div
        v-if="canManage"
        class="actions"
      >
        <button
          type="submit"
          class="primary"
          :disabled="saving"
        >
          {{ t('sites.search.save') }}
        </button>
      </div>
    </form>
  </section>
</template>
