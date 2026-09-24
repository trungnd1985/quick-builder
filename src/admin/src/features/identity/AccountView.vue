<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError } from '../../api/request'
import { identityApi } from '../../api/identity'

// The signed-in user's own account (002 FR-012, FR-012a): change the password — which
// signs out every other session — or sign out the other sessions on their own.

const { t, te } = useI18n()

const current = ref('')
const next = ref('')
const busy = ref(false)
const message = ref<string | null>(null)
const error = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

async function run(action: () => Promise<void>, done: string): Promise<void> {
  busy.value = true
  message.value = null
  error.value = null
  fieldErrors.value = {}

  try {
    await action()
    message.value = done
  } catch (caught) {
    fieldErrors.value = caught instanceof ApiError ? caught.fieldErrors : {}
    const key = caught instanceof ApiError && caught.errorCode ? `identity.errors.${caught.errorCode}` : ''
    error.value = key && te(key)
      ? t(key, { seconds: caught instanceof ApiError ? caught.retryAfterSeconds ?? 0 : 0 })
      : caught instanceof Error ? caught.message : String(caught)
  } finally {
    busy.value = false
  }
}

function changePassword(): Promise<void> {
  return run(async () => {
    await identityApi.changeOwnPassword(current.value, next.value)
    current.value = ''
    next.value = ''
  }, t('identity.account.passwordChanged'))
}

function signOutOthers(): Promise<void> {
  return run(() => identityApi.revokeOtherSessions(), t('identity.account.othersSignedOut'))
}
</script>

<template>
  <section aria-labelledby="account-heading">
    <h1 id="account-heading">
      {{ t('identity.account.title') }}
    </h1>

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

    <form
      class="card"
      novalidate
      @submit.prevent="changePassword"
    >
      <h2>{{ t('identity.account.changePassword') }}</h2>

      <div class="field">
        <label for="current-password">{{ t('identity.account.current') }}</label>
        <input
          id="current-password"
          v-model="current"
          type="password"
          autocomplete="current-password"
          :aria-invalid="Boolean(fieldErrors.CurrentPassword)"
          required
        >
      </div>

      <div class="field">
        <label for="next-password">{{ t('identity.account.next') }}</label>
        <input
          id="next-password"
          v-model="next"
          type="password"
          autocomplete="new-password"
          :aria-invalid="Boolean(fieldErrors.NewPassword)"
          aria-describedby="next-password-help next-password-error"
          required
        >
        <p
          id="next-password-help"
          class="help"
        >
          {{ t('identity.account.nextHelp') }}
        </p>
        <p
          v-if="fieldErrors.NewPassword"
          id="next-password-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.NewPassword.join(' ') }}
        </p>
      </div>

      <div class="actions">
        <button
          type="submit"
          class="primary"
          :disabled="busy"
        >
          {{ t('identity.account.submit') }}
        </button>
      </div>
    </form>

    <section
      class="card"
      aria-labelledby="sessions-heading"
    >
      <h2 id="sessions-heading">
        {{ t('identity.account.sessions') }}
      </h2>
      <p class="help">
        {{ t('identity.account.sessionsHelp') }}
      </p>
      <button
        type="button"
        :disabled="busy"
        @click="signOutOthers"
      >
        {{ t('identity.account.signOutOthers') }}
      </button>
    </section>
  </section>
</template>
