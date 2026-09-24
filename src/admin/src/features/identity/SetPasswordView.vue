<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../../api/request'
import { identityApi } from '../../api/identity'

// Using a set-password link (002 FR-011, FR-011a; research R-10). The token arrives in
// the URL fragment, which browsers never send to a server. It is read once and then
// removed from the address bar, so it is not left in history or on screen.

const { t } = useI18n()
const router = useRouter()
const route = useRoute()

type State = 'checking' | 'ready' | 'invalid' | 'done'
const state = ref<State>('checking')
const token = ref('')
const password = ref('')
const submitting = ref(false)
const formError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})

onMounted(async () => {
  token.value = new URLSearchParams(route.hash.replace(/^#/, '')).get('token') ?? ''
  await router.replace({ path: route.path, query: route.query, hash: '' })

  if (!token.value) {
    state.value = 'invalid'
    return
  }

  try {
    await identityApi.checkLink(token.value)
    state.value = 'ready'
  } catch {
    state.value = 'invalid'
  }
})

async function submit(): Promise<void> {
  submitting.value = true
  formError.value = null
  fieldErrors.value = {}

  try {
    await identityApi.setPassword(token.value, password.value)
    state.value = 'done'
  } catch (caught) {
    if (caught instanceof ApiError && caught.status === 410) {
      state.value = 'invalid'
      return
    }
    fieldErrors.value = caught instanceof ApiError ? caught.fieldErrors : {}
    formError.value = caught instanceof Error ? caught.message : String(caught)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <section aria-labelledby="set-password-heading">
    <h1 id="set-password-heading">
      {{ t('identity.setPassword.title') }}
    </h1>

    <p
      v-if="state === 'checking'"
      class="status"
      role="status"
    >
      {{ t('identity.setPassword.checking') }}
    </p>

    <div
      v-else-if="state === 'invalid'"
      class="card error-card"
      role="alert"
    >
      <p>{{ t('identity.setPassword.invalid') }}</p>
    </div>

    <div
      v-else-if="state === 'done'"
      class="card"
      role="status"
    >
      <p>{{ t('identity.setPassword.done') }}</p>
      <button
        type="button"
        class="primary"
        @click="router.push({ name: 'sign-in' })"
      >
        {{ t('identity.setPassword.signIn') }}
      </button>
    </div>

    <form
      v-else
      class="card"
      novalidate
      @submit.prevent="submit"
    >
      <div class="field">
        <label for="new-password">{{ t('identity.setPassword.password') }}</label>
        <input
          id="new-password"
          v-model="password"
          type="password"
          autocomplete="new-password"
          :aria-invalid="Boolean(fieldErrors.NewPassword)"
          aria-describedby="new-password-help new-password-error"
          required
        >
        <p
          id="new-password-help"
          class="help"
        >
          {{ t('identity.setup.passwordHelp') }}
        </p>
        <p
          v-if="fieldErrors.NewPassword"
          id="new-password-error"
          class="error"
          role="alert"
        >
          {{ fieldErrors.NewPassword.join(' ') }}
        </p>
      </div>

      <p
        v-if="formError && !fieldErrors.NewPassword"
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
          {{ submitting ? t('identity.setPassword.submitting') : t('identity.setPassword.submit') }}
        </button>
      </div>
    </form>
  </section>
</template>
