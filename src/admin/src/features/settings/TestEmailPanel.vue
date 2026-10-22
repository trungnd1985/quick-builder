<script setup lang="ts">
// Outgoing email for Administrators (018 FR-037): whether it is set up, and a test email to their
// own address that shows what the mail server answered. The settings themselves live in the
// server's configuration, never in the admin.
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { formsApi } from '../../api/forms'
import { ApiError } from '../../api/request'
import { useAuthStore } from '../../app/authStore'

const { t } = useI18n()
const auth = useAuthStore()

const configured = ref<boolean | null>(null)
const sending = ref(false)
const outcome = ref<{ ok: boolean; text: string } | null>(null)

onMounted(async () => {
  configured.value = (await formsApi.emailStatus().catch(() => null))?.configured ?? null
})

async function send(): Promise<void> {
  sending.value = true
  outcome.value = null
  try {
    await formsApi.testEmail()
    outcome.value = { ok: true, text: t('settings.email.sent', { email: auth.user?.email ?? '' }) }
  } catch (caught) {
    const problem = caught instanceof ApiError ? (caught.problem as unknown as Record<string, unknown> | undefined) : undefined
    const reply = typeof problem?.serverReply === 'string' ? problem.serverReply : ''
    const text = caught instanceof ApiError && caught.errorCode === 'forms.email_not_configured'
      ? t('settings.email.notConfigured')
      : reply ? t('settings.email.failed', { reply }) : t('settings.email.failedNoReply')
    outcome.value = { ok: false, text }
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <section
    class="card"
    aria-labelledby="email-settings-heading"
  >
    <h2 id="email-settings-heading">
      {{ t('settings.email.heading') }}
    </h2>
    <p v-if="configured !== null">
      {{ configured ? t('settings.email.on') : t('settings.email.off') }}
    </p>
    <button
      v-if="configured"
      type="button"
      :disabled="sending"
      @click="send"
    >
      {{ sending ? t('settings.email.sending') : t('settings.email.send') }}
    </button>
    <p
      v-if="outcome"
      :class="outcome.ok ? 'notice' : 'error'"
      :role="outcome.ok ? 'status' : 'alert'"
    >
      {{ outcome.text }}
    </p>
  </section>
</template>
