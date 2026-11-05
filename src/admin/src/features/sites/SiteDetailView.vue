<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { sitesApi, type Site, type SiteScheme } from '../../api/sites'
import { useSitesStore } from '../../app/sitesStore'
import { useAuthStore } from '../../app/authStore'
import AvailabilityPanel from './AvailabilityPanel.vue'
import SearchSettingsPanel from './SearchSettingsPanel.vue'
import SearchVisibilityPanel from './SearchVisibilityPanel.vue'
import TransferTab from '../transfer/TransferTab.vue'
import HostNameList from './HostNameList.vue'
import LanguageSelect from './LanguageSelect.vue'
import { isOutOfDate, siteErrorMessage } from './siteErrors'
import { UNREAD_CAP, useUnreadCount } from '../forms/useUnreadCount'

// 009: one site's settings (US1), host names (US2) and availability (US4). Everyone who
// can see the site sees every setting; only what the server says they may change is
// editable (FR-023), and the server checks every change regardless.

const { t, te } = useI18n()
const route = useRoute()
const sites = useSitesStore()
const auth = useAuthStore()

const siteId = computed(() => String(route.params.siteId))
// 018: the unread badge beside Submissions.
const { count: unread } = useUnreadCount(siteId)
const site = ref<Site | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

// The form edits a copy, so a save that fails leaves the displayed site as it was.
const name = ref('')
const scheme = ref<SiteScheme>('https')
const defaultLanguage = ref('en')
const primaryHostName = ref('')
const saving = ref(false)
const saveError = ref<string | null>(null)
const outOfDate = ref(false)
const saveStatus = ref('')

const canEditSettings = computed(() => site.value?.permissions?.canManageSettings === true)
const canChangeAnything = computed(() => {
  const permissions = site.value?.permissions
  return permissions !== undefined
    && (permissions.canManageSettings || permissions.canManageHostNames || permissions.canManageAvailability)
})

function show(next: Site): void {
  site.value = next
  name.value = next.name
  scheme.value = next.scheme
  defaultLanguage.value = next.defaultLanguage
  primaryHostName.value = next.primaryHostName
  sites.replace(next)
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null

  try {
    show(await sitesApi.get(siteId.value))
    outOfDate.value = false
  } catch (caught) {
    loadError.value = siteErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

async function save(): Promise<void> {
  if (!site.value) {
    return
  }

  saving.value = true
  saveError.value = null
  saveStatus.value = ''
  outOfDate.value = false

  try {
    show(await sitesApi.putSettings(site.value.id, {
      name: name.value,
      scheme: scheme.value,
      defaultLanguage: defaultLanguage.value,
      primaryHostName: primaryHostName.value,
      version: site.value.version,
    }))
    saveStatus.value = t('sites.settings.saved')
  } catch (caught) {
    if (isOutOfDate(caught)) {
      outOfDate.value = true
    } else {
      saveError.value = siteErrorMessage(t, te, caught)
    }
  } finally {
    saving.value = false
  }
}

onMounted(load)
watch(siteId, load)
</script>

<template>
  <section aria-labelledby="site-heading">
    <p>
      <RouterLink :to="{ name: 'sites' }">
        {{ t('sites.detail.back') }}
      </RouterLink>
    </p>

    <p
      v-if="loading && !site"
      class="status"
      role="status"
    >
      {{ t('sites.detail.loading') }}
    </p>

    <div
      v-else-if="loadError && !site"
      class="card error-card"
      role="alert"
    >
      <h1 id="site-heading">
        {{ t('sites.detail.errorTitle') }}
      </h1>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('sites.detail.retry') }}
      </button>
    </div>

    <template v-else-if="site">
      <header class="page-header">
        <div>
          <h1 id="site-heading">
            {{ site.name }}
            <span
              class="badge"
              :class="{ 'badge-warning': site.status === 'disabled' }"
            >{{ site.status === 'disabled' ? t('sites.status.disabled') : t('sites.status.enabled') }}</span>
            <span
              v-if="site.scheme === 'http'"
              class="badge badge-warning"
              :title="t('sites.httpBadgeLabel')"
              :aria-label="t('sites.httpBadgeLabel')"
            >{{ t('sites.httpBadge') }}</span>
          </h1>
          <p class="subtitle">
            {{ t('sites.detail.baseUrl') }}: <code>{{ site.baseUrl }}</code>
          </p>
        </div>
      </header>

      <!-- 010: every site role can open these; the server checks each capability. -->
      <nav
        class="site-sections"
        :aria-label="t('sites.detail.sections')"
      >
        <RouterLink :to="{ name: 'site-pages', params: { siteId: site.id } }">
          {{ t('sites.detail.pages') }}
        </RouterLink>
        <RouterLink :to="{ name: 'site-components', params: { siteId: site.id } }">
          {{ t('sites.detail.components') }}
        </RouterLink>
        <!-- 024: menus, shown by Navigation blocks; Editors see them read-only. -->
        <RouterLink :to="{ name: 'site-menus', params: { siteId: site.id } }">
          {{ t('sites.detail.menus') }}
        </RouterLink>
        <!-- 018: every site role reads submissions; the badge counts unread ones. -->
        <RouterLink :to="{ name: 'site-submissions', params: { siteId: site.id } }">
          {{ t('sites.detail.submissions') }}
          <span
            v-if="unread > 0"
            class="badge"
          >{{ unread >= UNREAD_CAP ? t('sites.detail.unreadMany') : t('sites.detail.unread', { count: unread }) }}</span>
        </RouterLink>
        <RouterLink :to="{ name: 'site-collections', params: { siteId: site.id } }">
          {{ t('sites.detail.collections') }}
        </RouterLink>
        <RouterLink :to="{ name: 'site-media', params: { siteId: site.id } }">
          {{ t('sites.detail.media') }}
        </RouterLink>
        <RouterLink :to="{ name: 'site-design', params: { siteId: site.id } }">
          {{ t('sites.detail.design') }}
        </RouterLink>
        <RouterLink :to="{ name: 'site-redirects', params: { siteId: site.id } }">
          {{ t('sites.detail.redirects') }}
        </RouterLink>
        <!-- 012 FR-017: the publish log is for Administrators; the server checks regardless. -->
        <RouterLink
          v-if="auth.isAdministrator"
          :to="{ name: 'site-publish-log', params: { siteId: site.id } }"
        >
          {{ t('publishing.log.title') }}
        </RouterLink>
      </nav>

      <!-- The spec's edge case: a disabled site stays open here, clearly marked. -->
      <p
        v-if="site.status === 'disabled'"
        class="card notice"
        role="note"
      >
        {{ t('sites.detail.disabledNotice') }}
      </p>

      <p
        v-if="!canChangeAnything"
        class="help"
      >
        {{ t('sites.detail.readOnly') }}
      </p>

      <form
        class="card"
        novalidate
        aria-labelledby="settings-heading"
        @submit.prevent="save"
      >
        <h2 id="settings-heading">
          {{ t('sites.settings.heading') }}
        </h2>

        <div class="field">
          <label for="site-settings-name">{{ t('sites.settings.name') }}</label>
          <input
            id="site-settings-name"
            v-model="name"
            type="text"
            :disabled="!canEditSettings"
            required
          >
        </div>

        <div class="field">
          <label for="site-settings-scheme">{{ t('sites.settings.scheme') }}</label>
          <select
            id="site-settings-scheme"
            v-model="scheme"
            :disabled="!canEditSettings"
            :aria-describedby="scheme === 'http' ? 'site-settings-scheme-warning' : undefined"
          >
            <option value="https">
              {{ t('sites.settings.https') }}
            </option>
            <option value="http">
              {{ t('sites.settings.http') }}
            </option>
          </select>
          <p
            v-if="scheme === 'http'"
            id="site-settings-scheme-warning"
            class="warning"
          >
            {{ t('sites.settings.httpWarning') }}
          </p>
        </div>

        <div class="field">
          <label for="site-settings-language">{{ t('sites.settings.language') }}</label>
          <LanguageSelect
            id="site-settings-language"
            v-model="defaultLanguage"
            :disabled="!canEditSettings"
          />
        </div>

        <div class="field">
          <label for="site-settings-primary">{{ t('sites.settings.primary') }}</label>
          <select
            id="site-settings-primary"
            v-model="primaryHostName"
            :disabled="!canEditSettings"
            aria-describedby="site-settings-primary-help"
          >
            <option
              v-for="hostName in site.hostNameDetails"
              :key="hostName.hostName"
              :value="hostName.hostName"
            >
              {{ hostName.displayName }}
            </option>
          </select>
          <p
            id="site-settings-primary-help"
            class="help"
          >
            {{ t('sites.settings.primaryHelp') }}
          </p>
        </div>

        <div
          v-if="outOfDate"
          class="error"
          role="alert"
        >
          <p>{{ t('sites.settings.outOfDate') }}</p>
          <button
            type="button"
            @click="load()"
          >
            {{ t('sites.settings.reload') }}
          </button>
        </div>
        <p
          v-if="saveError"
          class="error"
          role="alert"
        >
          {{ saveError }}
        </p>
        <p
          class="status"
          role="status"
        >
          {{ saveStatus }}
        </p>

        <div
          v-if="canEditSettings"
          class="actions"
        >
          <button
            type="submit"
            class="primary"
            :disabled="saving"
          >
            {{ saving ? t('sites.settings.saving') : t('sites.settings.save') }}
          </button>
        </div>
      </form>

      <HostNameList
        :site="site"
        @updated="show"
        @reload="load()"
      />

      <AvailabilityPanel
        :site="site"
        @updated="show"
      />

      <SearchSettingsPanel
        :site="site"
        @updated="show"
      />

      <SearchVisibilityPanel
        :site="site"
        @updated="show"
      />

      <TransferTab :site="site" />
    </template>
  </section>
</template>
