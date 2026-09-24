<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../../api/request'
import { identityApi } from '../../api/identity'
import { useAuthStore } from '../../app/authStore'
import { safeReturnTo } from '../../app/returnTo'

// Sign-in (002 US2). A failed attempt gets one message whatever went wrong — the server
// never says whether the address exists (FR-014), and neither does this screen.

const { t, te } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const email = ref('')
const password = ref('')
const submitting = ref(false)
const formError = ref<string | null>(null)

async function submit(): Promise<void> {
  submitting.value = true
  formError.value = null

  try {
    const me = await identityApi.signIn(email.value, password.value)
    auth.signedIn(me)
    await router.push(safeReturnTo(router, route.query.returnTo))
  } catch (caught) {
    formError.value = describe(caught)
  } finally {
    submitting.value = false
  }
}

function describe(caught: unknown): string {
  if (!(caught instanceof ApiError)) {
    return t('identity.signIn.errors.unexpected')
  }

  if (caught.errorCode === 'request.invalid') {
    return t('identity.signIn.errors.missing')
  }

  const key = `identity.errors.${caught.errorCode}`
  return te(key)
    ? t(key, { seconds: caught.retryAfterSeconds ?? 0 })
    : caught.message
}
</script>

<template>
  <section aria-labelledby="sign-in-heading">
    <h1 id="sign-in-heading">
      {{ t('identity.signIn.title') }}
    </h1>

    <form
      class="card"
      novalidate
      @submit.prevent="submit"
    >
      <div class="field">
        <label for="sign-in-email">{{ t('identity.signIn.email') }}</label>
        <input
          id="sign-in-email"
          v-model="email"
          type="email"
          autocomplete="username"
          required
        >
      </div>

      <div class="field">
        <label for="sign-in-password">{{ t('identity.signIn.password') }}</label>
        <input
          id="sign-in-password"
          v-model="password"
          type="password"
          autocomplete="current-password"
          required
        >
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
          {{ submitting ? t('identity.signIn.submitting') : t('identity.signIn.submit') }}
        </button>
      </div>
    </form>
  </section>
</template>
