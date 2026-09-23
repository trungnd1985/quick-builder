<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ApiError } from '../../api/request'
import { identityApi } from '../../api/identity'
import { useAuthStore } from '../../app/authStore'

// The setup wizard (002 US1): the setup code from the application's console output, then
// the first administrator's name, email address and password. Succeeding signs the
// operator in; the server makes sure it can only ever succeed once.

const { t, te } = useI18n()
const router = useRouter()
const auth = useAuthStore()

const setupCode = ref('')
const displayName = ref('')
const email = ref('')
const password = ref('')

const submitting = ref(false)
const formError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

async function submit(): Promise<void> {
  submitting.value = true
  formError.value = null
  fieldErrors.value = {}

  try {
    const me = await identityApi.setup({
      setupCode: setupCode.value,
      displayName: displayName.value,
      email: email.value,
      password: password.value,
    })
    auth.signedIn(me)
    await router.push({ name: 'sites' })
  } catch (caught) {
    if (caught instanceof ApiError && caught.errorCode === 'identity.setup_already_completed') {
      // Someone else finished setup first. There is nothing to do here any more.
      auth.signedOut()
      auth.setupRequired = false
      await router.push({ name: 'sites' })
      return
    }

    formError.value = describe(caught)
    fieldErrors.value = caught instanceof ApiError ? caught.fieldErrors : {}
  } finally {
    submitting.value = false
  }
}

function describe(caught: unknown): string {
  if (!(caught instanceof ApiError)) {
    return t('identity.setup.errors.unexpected')
  }

  if (caught.errorCode === 'request.invalid') {
    return t('identity.setup.errors.invalid')
  }

  const key = `identity.errors.${caught.errorCode}`
  return te(key)
    ? t(key, { seconds: caught.retryAfterSeconds ?? 0 })
    : caught.message
}
</script>

<template>
  <section aria-labelledby="setup-heading">
    <h1 id="setup-heading">
      {{ t('identity.setup.title') }}
    </h1>
    <p class="subtitle">
      {{ t('identity.setup.intro') }}
    </p>

    <!--
      FR-032 / 001 FR-045b: every control has a label, and each message is tied to its input
      through aria-describedby so a screen reader announces it.
    -->
    <form
      class="card"
      novalidate
      @submit.prevent="submit"
    >
      <div class="field">
        <label for="setup-code">{{ t('identity.setup.code') }}</label>
        <input
          id="setup-code"
          v-model="setupCode"
          type="text"
          autocomplete="off"
          autocapitalize="characters"
          spellcheck="false"
          :aria-invalid="Boolean(fieldErrors.SetupCode)"
          aria-describedby="setup-code-help"
          required
        >
        <p
          id="setup-code-help"
          class="help"
        >
          {{ t('identity.setup.codeHelp') }}
        </p>
      </div>

      <div class="field">
        <label for="setup-name">{{ t('identity.setup.displayName') }}</label>
        <input
          id="setup-name"
          v-model="displayName"
          type="text"
          autocomplete="name"
          :aria-invalid="Boolean(fieldErrors.DisplayName)"
          aria-describedby="setup-name-error"
          required
        >
        <p
          v-if="fieldErrors.DisplayName"
          id="setup-name-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.DisplayName.join(' ') }}
        </p>
      </div>

      <div class="field">
        <label for="setup-email">{{ t('identity.setup.email') }}</label>
        <input
          id="setup-email"
          v-model="email"
          type="email"
          autocomplete="username"
          :aria-invalid="Boolean(fieldErrors.Email)"
          aria-describedby="setup-email-error"
          required
        >
        <p
          v-if="fieldErrors.Email"
          id="setup-email-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.Email.join(' ') }}
        </p>
      </div>

      <div class="field">
        <label for="setup-password">{{ t('identity.setup.password') }}</label>
        <input
          id="setup-password"
          v-model="password"
          type="password"
          autocomplete="new-password"
          :aria-invalid="Boolean(fieldErrors.Password)"
          aria-describedby="setup-password-help setup-password-error"
          required
        >
        <p
          id="setup-password-help"
          class="help"
        >
          {{ t('identity.setup.passwordHelp') }}
        </p>
        <p
          v-if="fieldErrors.Password"
          id="setup-password-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.Password.join(' ') }}
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
          {{ submitting ? t('identity.setup.submitting') : t('identity.setup.submit') }}
        </button>
      </div>
    </form>
  </section>
</template>
