<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { sitesApi, type Site } from '../../api/sites'
import ConfirmDialog from './ConfirmDialog.vue'
import { siteErrorMessage } from './siteErrors'

// 009 US4: take a site offline and back, keeping everything. Visitors to a disabled site
// see exactly what they would see for a host name no site holds (clarification Q1), and
// the confirmation says so before anyone commits to it.

const props = defineProps<{ site: Site }>()
const emit = defineEmits<{ updated: [site: Site] }>()

const { t, te } = useI18n()

const isEnabled = computed(() => props.site.status !== 'disabled')
const canManage = computed(() => props.site.permissions?.canManageAvailability === true)

const confirming = ref(false)
const working = ref(false)
const error = ref<string | null>(null)
const status = ref('')
const trigger = ref<HTMLButtonElement | null>(null)

async function close(): Promise<void> {
  confirming.value = false
  await nextTick()
  trigger.value?.focus()
}

async function confirm(): Promise<void> {
  working.value = true
  error.value = null

  try {
    const saved = isEnabled.value
      ? await sitesApi.disable(props.site.id)
      : await sitesApi.enable(props.site.id)
    status.value = saved.status === 'disabled'
      ? t('sites.availability.disabled', { name: saved.name })
      : t('sites.availability.enabled', { name: saved.name })
    emit('updated', saved)
  } catch (caught) {
    error.value = siteErrorMessage(t, te, caught)
  } finally {
    working.value = false
    await close()
  }
}
</script>

<template>
  <section
    class="card"
    aria-labelledby="availability-heading"
  >
    <h2 id="availability-heading">
      {{ t('sites.availability.heading') }}
    </h2>
    <p>
      {{ isEnabled ? t('sites.availability.enabledBody') : t('sites.availability.disabledBody') }}
    </p>

    <div
      v-if="canManage"
      class="actions"
    >
      <button
        ref="trigger"
        type="button"
        :class="isEnabled ? 'danger' : 'primary'"
        @click="confirming = true"
      >
        {{ isEnabled ? t('sites.availability.disable') : t('sites.availability.enable') }}
      </button>
    </div>

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      class="status"
      role="status"
    >
      {{ status }}
    </p>

    <ConfirmDialog
      v-if="confirming"
      dialog-id="site-availability"
      :title="isEnabled
        ? t('sites.availability.confirmDisableTitle', { name: site.name })
        : t('sites.availability.confirmEnableTitle', { name: site.name })"
      :body="isEnabled ? t('sites.availability.confirmDisableBody') : t('sites.availability.confirmEnableBody')"
      :confirm-label="isEnabled ? t('sites.availability.confirmDisable') : t('sites.availability.confirmEnable')"
      :cancel-label="t('sites.availability.cancel')"
      :busy-label="t('sites.availability.working')"
      :busy="working"
      :danger="isEnabled"
      @confirm="confirm"
      @cancel="close"
    />
  </section>
</template>
