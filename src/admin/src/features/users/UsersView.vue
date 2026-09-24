<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError } from '../../api/request'
import { usersApi, type Grants, type IssuedLink, type User } from '../../api/identity'
import CopyLinkDialog from './CopyLinkDialog.vue'
import GrantsEditor from './GrantsEditor.vue'

// Every account, and creating one (002 US3). The server refuses anyone who may not manage
// users; this screen is only shown to administrators as a courtesy (FR-024).

const { t, te } = useI18n()

const users = ref<User[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)

const showCreate = ref(false)
const displayName = ref('')
const email = ref('')
const grants = ref<Grants>({ administrator: false, siteRoles: [] })
const submitting = ref(false)
const formError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

const issued = ref<{ link: IssuedLink; name: string } | null>(null)

onMounted(load)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    users.value = await usersApi.list()
  } catch (caught) {
    loadError.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    loading.value = false
  }
}

async function create(): Promise<void> {
  submitting.value = true
  formError.value = null
  fieldErrors.value = {}

  try {
    const created = await usersApi.create(displayName.value, email.value, grants.value)
    users.value = [...users.value, created.user].sort((a, b) => a.displayName.localeCompare(b.displayName))
    issued.value = { link: created.setPasswordLink, name: created.user.displayName }
    showCreate.value = false
    displayName.value = ''
    email.value = ''
    grants.value = { administrator: false, siteRoles: [] }
  } catch (caught) {
    fieldErrors.value = caught instanceof ApiError ? caught.fieldErrors : {}
    formError.value = describe(caught)
  } finally {
    submitting.value = false
  }
}

function describe(caught: unknown): string {
  if (caught instanceof ApiError && caught.errorCode) {
    const key = `identity.errors.${caught.errorCode}`
    if (te(key)) {
      return t(key)
    }
  }
  return caught instanceof Error ? caught.message : String(caught)
}

function roleSummary(user: User): string {
  if (user.grants.administrator) {
    return t('identity.users.roles.administrator')
  }
  return user.grants.siteRoles.length === 0
    ? t('identity.users.roles.none')
    : t('identity.users.roles.sites', { count: user.grants.siteRoles.length })
}
</script>

<template>
  <section aria-labelledby="users-heading">
    <header class="page-header">
      <div>
        <h1 id="users-heading">
          {{ t('identity.users.title') }}
        </h1>
        <p class="subtitle">
          {{ t('identity.users.subtitle') }}
        </p>
      </div>
      <button
        type="button"
        class="primary"
        @click="showCreate = !showCreate"
      >
        {{ t('identity.users.create.title') }}
      </button>
    </header>

    <CopyLinkDialog
      v-if="issued"
      :link="issued.link"
      :user-name="issued.name"
      @close="issued = null"
    />

    <form
      v-if="showCreate"
      class="card"
      novalidate
      @submit.prevent="create"
    >
      <h2>{{ t('identity.users.create.title') }}</h2>

      <div class="field">
        <label for="new-user-name">{{ t('identity.users.fields.displayName') }}</label>
        <input
          id="new-user-name"
          v-model="displayName"
          type="text"
          :aria-invalid="Boolean(fieldErrors.DisplayName)"
          aria-describedby="new-user-name-error"
          required
        >
        <p
          v-if="fieldErrors.DisplayName"
          id="new-user-name-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.DisplayName.join(' ') }}
        </p>
      </div>

      <div class="field">
        <label for="new-user-email">{{ t('identity.users.fields.email') }}</label>
        <input
          id="new-user-email"
          v-model="email"
          type="email"
          :aria-invalid="Boolean(fieldErrors.Email)"
          aria-describedby="new-user-email-error"
          required
        >
        <p
          v-if="fieldErrors.Email"
          id="new-user-email-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.Email.join(' ') }}
        </p>
      </div>

      <GrantsEditor v-model="grants" />

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
          {{ submitting ? t('identity.users.create.submitting') : t('identity.users.create.submit') }}
        </button>
        <button
          type="button"
          @click="showCreate = false"
        >
          {{ t('identity.users.create.cancel') }}
        </button>
      </div>
    </form>

    <p
      v-if="loading"
      class="status"
      role="status"
    >
      {{ t('identity.users.loading') }}
    </p>

    <div
      v-else-if="loadError"
      class="card error-card"
      role="alert"
    >
      <h2>{{ t('identity.users.loadError') }}</h2>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load"
      >
        {{ t('identity.users.retry') }}
      </button>
    </div>

    <table
      v-else
      class="sites-table"
    >
      <caption class="visually-hidden">
        {{ t('identity.users.title') }}
      </caption>
      <thead>
        <tr>
          <th scope="col">
            {{ t('identity.users.fields.displayName') }}
          </th>
          <th scope="col">
            {{ t('identity.users.fields.email') }}
          </th>
          <th scope="col">
            {{ t('identity.users.fields.status') }}
          </th>
          <th scope="col">
            {{ t('identity.users.fields.roles') }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="user in users"
          :key="user.id"
        >
          <td>
            <RouterLink :to="{ name: 'user', params: { id: user.id } }">
              {{ user.displayName }}
            </RouterLink>
          </td>
          <td>{{ user.email }}</td>
          <td>
            {{ user.status === 'active'
              ? (user.hasPassword ? t('identity.users.status.active') : t('identity.users.status.invited'))
              : t('identity.users.status.deactivated') }}
          </td>
          <td>{{ roleSummary(user) }}</td>
        </tr>
      </tbody>
    </table>
  </section>
</template>
