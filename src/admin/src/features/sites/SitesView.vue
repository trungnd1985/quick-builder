<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../app/authStore'
import { useSitesStore } from '../../app/sitesStore'
import type { SiteScheme } from '../../api/sites'
import LanguageSelect from './LanguageSelect.vue'

const { t, d } = useI18n()
const store = useSitesStore()
const auth = useAuthStore()

// Only administrators create sites (002 FR-027). Hiding the form is a courtesy; the
// server refuses anyone else regardless (FR-024).
const canCreate = computed(() => auth.isAdministrator)

// A signed-in user with no role anywhere sees an explanation rather than an empty list
// that suggests creating a site they cannot create (002 spec edge case).
const hasNoAccess = computed(
  () => auth.user !== null && !auth.user.isAdministrator && auth.user.siteRoles.length === 0,
)

const showCreate = ref(false)
const name = ref('')
const hostName = ref('')
const scheme = ref<SiteScheme>('https')
const defaultLanguage = ref('en')
const submitting = ref(false)
const formError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

onMounted(() => store.load())

async function submit(): Promise<void> {
  submitting.value = true
  formError.value = null
  fieldErrors.value = {}

  const result = await store.create(name.value, hostName.value, {
    scheme: scheme.value,
    defaultLanguage: defaultLanguage.value,
  })

  submitting.value = false

  if (result.ok) {
    name.value = ''
    hostName.value = ''
    scheme.value = 'https'
    defaultLanguage.value = 'en'
    showCreate.value = false
  } else {
    formError.value = result.message
    fieldErrors.value = result.fields
  }
}
</script>

<template>
  <section aria-labelledby="sites-heading">
    <header class="page-header">
      <div>
        <h1 id="sites-heading">
          {{ t('sites.title') }}
        </h1>
        <p class="subtitle">
          {{ t('sites.subtitle') }}
        </p>
      </div>
      <RouterLink
        v-if="canCreate"
        class="button-link"
        :to="{ name: 'site-import' }"
      >
        {{ t('transfer.import.entry') }}
      </RouterLink>
      <button
        v-if="canCreate"
        type="button"
        class="primary"
        @click="showCreate = !showCreate"
      >
        {{ t('sites.create.title') }}
      </button>
    </header>

    <!--
      FR-045b: the form is keyboard-reachable, every control has an associated label, and
      validation messages are tied to their input via aria-describedby so a screen reader
      announces them rather than leaving them as decoration.
    -->
    <form
      v-if="canCreate && showCreate"
      class="card"
      novalidate
      @submit.prevent="submit"
    >
      <h2>{{ t('sites.create.title') }}</h2>

      <div class="field">
        <label for="site-name">{{ t('sites.create.name') }}</label>
        <input
          id="site-name"
          v-model="name"
          type="text"
          :placeholder="t('sites.create.namePlaceholder')"
          :aria-invalid="Boolean(fieldErrors.Name)"
          aria-describedby="site-name-error"
          required
        >
        <p
          v-if="fieldErrors.Name"
          id="site-name-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.Name.join(' ') }}
        </p>
      </div>

      <div class="field">
        <label for="site-host">{{ t('sites.create.hostName') }}</label>
        <input
          id="site-host"
          v-model="hostName"
          type="text"
          :placeholder="t('sites.create.hostNamePlaceholder')"
          :aria-invalid="Boolean(fieldErrors.HostNames)"
          aria-describedby="site-host-help site-host-error"
          required
        >
        <p
          id="site-host-help"
          class="help"
        >
          {{ t('sites.create.hostNameHelp') }}
        </p>
        <p
          v-if="fieldErrors.HostNames"
          id="site-host-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.HostNames.join(' ') }}
        </p>
      </div>

      <div class="field">
        <label for="site-scheme">{{ t('sites.create.scheme') }}</label>
        <select
          id="site-scheme"
          v-model="scheme"
          :aria-describedby="scheme === 'http' ? 'site-scheme-warning' : undefined"
        >
          <option value="https">
            {{ t('sites.create.schemeHttps') }}
          </option>
          <option value="http">
            {{ t('sites.create.schemeHttp') }}
          </option>
        </select>
        <p
          v-if="scheme === 'http'"
          id="site-scheme-warning"
          class="warning"
        >
          {{ t('sites.settings.httpWarning') }}
        </p>
      </div>

      <div class="field">
        <label for="site-language">{{ t('sites.create.language') }}</label>
        <LanguageSelect
          id="site-language"
          v-model="defaultLanguage"
          describedby="site-language-help"
        />
        <p
          id="site-language-help"
          class="help"
        >
          {{ t('sites.create.languageHelp') }}
        </p>
      </div>

      <p
        v-if="formError"
        class="error"
        role="alert"
      >
        {{ formError }}
      </p>

      <div class="actions">
        <button
          type="submit"
          class="primary"
          :disabled="submitting"
        >
          {{ submitting ? t('sites.create.submitting') : t('sites.create.submit') }}
        </button>
        <button
          type="button"
          @click="showCreate = false"
        >
          {{ t('sites.create.cancel') }}
        </button>
      </div>
    </form>

    <!-- Loading, error and empty are three distinct states, never conflated (FR-045). -->
    <p
      v-if="store.loading"
      class="status"
      role="status"
    >
      {{ t('sites.loading') }}
    </p>

    <div
      v-else-if="store.error"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('sites.error.title') }}</h2>
      <p>{{ store.error }}</p>
      <button
        type="button"
        @click="store.load()"
      >
        {{ t('sites.error.retry') }}
      </button>
    </div>

    <div
      v-else-if="store.sites.length === 0 && hasNoAccess"
      class="card empty"
    >
      <h2>{{ t('sites.noAccess.title') }}</h2>
      <p>{{ t('sites.noAccess.body') }}</p>
    </div>

    <div
      v-else-if="store.sites.length === 0"
      class="card empty"
    >
      <h2>{{ t('sites.empty.title') }}</h2>
      <p>{{ t('sites.empty.body') }}</p>
    </div>

    <table
      v-else
      class="sites-table"
    >
      <caption class="visually-hidden">
        {{ t('sites.title') }}
      </caption>
      <thead>
        <tr>
          <th scope="col">
            {{ t('sites.table.name') }}
          </th>
          <th scope="col">
            {{ t('sites.table.status') }}
          </th>
          <th scope="col">
            {{ t('sites.table.hostNames') }}
          </th>
          <th scope="col">
            {{ t('sites.table.created') }}
          </th>
          <th scope="col">
            {{ t('sites.table.media') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="site in store.sites"
          :key="site.id"
        >
          <td>
            <!-- 009: every site member can open the settings; editing depends on permissions. -->
            <RouterLink
              :to="{ name: 'site', params: { siteId: site.id } }"
              :aria-label="t('sites.table.open', { name: site.name })"
            >
              {{ site.name }}
            </RouterLink>
          </td>
          <td>
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
          </td>
          <td>{{ (site.hostNameDetails?.map((h) => h.displayName) ?? site.hostNames).join(', ') }}</td>
          <td>
            <time :datetime="site.createdAtUtc">{{ d(new Date(site.createdAtUtc), 'short') }}</time>
          </td>
          <td>
            <!-- 003: every site role can open the library; the server checks the capability. -->
            <RouterLink
              :to="{ name: 'site-media', params: { siteId: site.id } }"
              :aria-label="t('sites.table.openMedia', { name: site.name })"
            >
              {{ t('sites.table.media') }}
            </RouterLink>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>
