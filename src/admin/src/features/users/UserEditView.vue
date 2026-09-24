<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ApiError } from '../../api/request'
import { usersApi, type Grants, type IssuedLink, type User } from '../../api/identity'
import CopyLinkDialog from './CopyLinkDialog.vue'
import GrantsEditor from './GrantsEditor.vue'

// One account (002 US3, US4): name and email, roles, activation, a new set-password link,
// and "sign out everywhere". Every change applies on the user's next request.

const { t, te } = useI18n()
const route = useRoute()
const id = String(route.params.id)

const user = ref<User | null>(null)
const loadError = ref<string | null>(null)

const displayName = ref('')
const email = ref('')
const grants = ref<Grants>({ administrator: false, siteRoles: [] })

const busy = ref(false)
const message = ref<string | null>(null)
const error = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})
const issued = ref<IssuedLink | null>(null)

onMounted(async () => {
  try {
    show(await usersApi.get(id))
  } catch (caught) {
    loadError.value = caught instanceof Error ? caught.message : String(caught)
  }
})

function show(value: User): void {
  user.value = value
  displayName.value = value.displayName
  email.value = value.email
  grants.value = { administrator: value.grants.administrator, siteRoles: [...value.grants.siteRoles] }
}

/** Runs one change, showing what happened or why it did not. */
async function act(change: () => Promise<User | void>, done: string): Promise<void> {
  busy.value = true
  message.value = null
  error.value = null
  fieldErrors.value = {}

  try {
    const updated = await change()
    if (updated) {
      show(updated)
    }
    message.value = done
  } catch (caught) {
    fieldErrors.value = caught instanceof ApiError ? caught.fieldErrors : {}
    const key = caught instanceof ApiError && caught.errorCode ? `identity.errors.${caught.errorCode}` : ''
    error.value = key && te(key) ? t(key) : caught instanceof Error ? caught.message : String(caught)
  } finally {
    busy.value = false
  }
}

function saveDetails(): Promise<void> {
  const patch: { displayName?: string; email?: string } = {}
  if (displayName.value !== user.value?.displayName) {
    patch.displayName = displayName.value
  }
  if (email.value !== user.value?.email) {
    patch.email = email.value
  }
  return act(() => usersApi.update(id, patch), t('identity.users.edit.saved'))
}

function saveGrants(): Promise<void> {
  return act(() => usersApi.replaceGrants(id, grants.value), t('identity.users.edit.rolesSaved'))
}

function toggleActive(): Promise<void> {
  return user.value?.status === 'active'
    ? act(() => usersApi.deactivate(id), t('identity.users.edit.deactivated'))
    : act(() => usersApi.reactivate(id), t('identity.users.edit.reactivated'))
}

function newLink(): Promise<void> {
  return act(async () => {
    issued.value = await usersApi.issueLink(id)
  }, t('identity.users.edit.linkIssued'))
}

function signOutEverywhere(): Promise<void> {
  return act(() => usersApi.revokeSessions(id), t('identity.users.edit.signedOut'))
}
</script>

<template>
  <section aria-labelledby="user-heading">
    <p
      v-if="loadError"
      class="error"
      role="alert"
    >
      {{ loadError }}
    </p>

    <template v-else-if="user">
      <header class="page-header">
        <div>
          <h1 id="user-heading">
            {{ user.displayName }}
          </h1>
          <p class="subtitle">
            {{ user.status === 'active' ? t('identity.users.status.active') : t('identity.users.status.deactivated') }}
          </p>
        </div>
        <RouterLink :to="{ name: 'users' }">
          {{ t('identity.users.edit.back') }}
        </RouterLink>
      </header>

      <p
        v-if="message"
        role="status"
      >
        {{ message }}
      </p>
      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>

      <CopyLinkDialog
        v-if="issued"
        :link="issued"
        :user-name="user.displayName"
        @close="issued = null"
      />

      <form
        class="card"
        novalidate
        @submit.prevent="saveDetails"
      >
        <h2>{{ t('identity.users.edit.details') }}</h2>

        <div class="field">
          <label for="edit-name">{{ t('identity.users.fields.displayName') }}</label>
          <input
            id="edit-name"
            v-model="displayName"
            type="text"
            :aria-invalid="Boolean(fieldErrors.DisplayName)"
            required
          >
        </div>

        <div class="field">
          <label for="edit-email">{{ t('identity.users.fields.email') }}</label>
          <input
            id="edit-email"
            v-model="email"
            type="email"
            :aria-invalid="Boolean(fieldErrors.Email)"
            aria-describedby="edit-email-help"
            required
          >
          <p
            id="edit-email-help"
            class="help"
          >
            {{ t('identity.users.edit.emailWarning') }}
          </p>
        </div>

        <div class="actions">
          <button
            type="submit"
            class="primary"
            :disabled="busy"
          >
            {{ t('identity.users.edit.save') }}
          </button>
        </div>
      </form>

      <form
        class="card"
        novalidate
        @submit.prevent="saveGrants"
      >
        <h2>{{ t('identity.users.edit.roles') }}</h2>
        <GrantsEditor v-model="grants" />
        <div class="actions">
          <button
            type="submit"
            class="primary"
            :disabled="busy"
          >
            {{ t('identity.users.edit.saveRoles') }}
          </button>
        </div>
      </form>

      <section
        class="card"
        aria-labelledby="access-heading"
      >
        <h2 id="access-heading">
          {{ t('identity.users.edit.access') }}
        </h2>
        <div class="actions">
          <button
            type="button"
            :disabled="busy || user.status !== 'active'"
            @click="newLink"
          >
            {{ t('identity.users.edit.newLink') }}
          </button>
          <button
            type="button"
            :disabled="busy"
            @click="signOutEverywhere"
          >
            {{ t('identity.users.edit.signOutEverywhere') }}
          </button>
          <button
            type="button"
            :disabled="busy"
            @click="toggleActive"
          >
            {{ user.status === 'active' ? t('identity.users.edit.deactivate') : t('identity.users.edit.reactivate') }}
          </button>
        </div>
      </section>
    </template>
  </section>
</template>
