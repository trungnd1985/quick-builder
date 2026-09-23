<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSitesStore } from '../../app/sitesStore'

const { t, d } = useI18n()
const store = useSitesStore()

const showCreate = ref(false)
const name = ref('')
const hostName = ref('')
const submitting = ref(false)
const formError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

onMounted(() => store.load())

async function submit(): Promise<void> {
  submitting.value = true
  formError.value = null
  fieldErrors.value = {}

  const result = await store.create(name.value, hostName.value)

  submitting.value = false

  if (result.ok) {
    name.value = ''
    hostName.value = ''
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
      <button
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
      v-if="showCreate"
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
            {{ t('sites.table.hostNames') }}
          </th>
          <th scope="col">
            {{ t('sites.table.created') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="site in store.sites"
          :key="site.id"
        >
          <td>{{ site.name }}</td>
          <td>{{ site.hostNames.join(', ') }}</td>
          <td>
            <time :datetime="site.createdAtUtc">{{ d(new Date(site.createdAtUtc), 'short') }}</time>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>
