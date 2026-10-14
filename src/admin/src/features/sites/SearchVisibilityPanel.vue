<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { sitesApi, type Site } from '../../api/sites'
import ConfirmDialog from './ConfirmDialog.vue'
import { siteErrorMessage } from './siteErrors'

// 017 US3, US6 (FR-014, FR-025, FR-027, research SE-07, SE-16, SE-17): whether search engines
// see the site, the Administrator's own robots.txt rules, and whether other host names
// redirect to the primary one. Administrators only: a mistake here takes the site out of
// search or cuts off a host name.

const props = defineProps<{ site: Site }>()
const emit = defineEmits<{ updated: [site: Site] }>()
const { t, te } = useI18n()

const canManage = computed(() => props.site.permissions?.canManageSearchVisibility === true)

const discourage = ref(false)
const robotsRules = ref('')
const redirectOtherHosts = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const status = ref('')
const confirming = ref(false)
const saveButton = ref<HTMLButtonElement | null>(null)

const otherHostNames = computed(() => props.site.hostNameDetails.filter((h) => !h.isPrimary).map((h) => h.displayName))
const primaryDisplay = computed(() => props.site.hostNameDetails.find((h) => h.isPrimary)?.displayName ?? props.site.primaryHostName)

watch(
  () => props.site,
  (site) => {
    discourage.value = site.searchVisibility?.discourageSearchEngines ?? false
    robotsRules.value = site.searchVisibility?.robotsRules ?? ''
    redirectOtherHosts.value = site.searchVisibility?.redirectOtherHostNames ?? false
  },
  { immediate: true },
)

/** Turning the host name redirect on asks first, naming what will redirect where (FR-014). */
function submit(): void {
  const turningOn = redirectOtherHosts.value && !(props.site.searchVisibility?.redirectOtherHostNames ?? false)
  if (turningOn && otherHostNames.value.length > 0) {
    confirming.value = true
    return
  }
  void save()
}

async function save(): Promise<void> {
  confirming.value = false
  saving.value = true
  error.value = null
  status.value = ''
  try {
    const saved = await sitesApi.putSearchVisibility(props.site.id, {
      discourageSearchEngines: discourage.value,
      robotsRules: robotsRules.value.trim() === '' ? null : robotsRules.value,
      redirectOtherHostNames: redirectOtherHosts.value,
      version: props.site.version,
    })
    status.value = t('sites.searchVisibility.saved')
    emit('updated', saved)
  } catch (caught) {
    error.value = siteErrorMessage(t, te, caught)
  } finally {
    saving.value = false
  }
}

async function cancelConfirm(): Promise<void> {
  confirming.value = false
  await nextTick()
  saveButton.value?.focus()
}
</script>

<template>
  <section
    class="card"
    aria-labelledby="site-visibility-heading"
  >
    <h2 id="site-visibility-heading">
      {{ t('sites.searchVisibility.heading') }}
    </h2>

    <form
      novalidate
      @submit.prevent="submit"
    >
      <label class="checkbox">
        <input
          v-model="discourage"
          type="checkbox"
          :disabled="!canManage"
          aria-describedby="site-visibility-discourage-help"
        >
        {{ t('sites.searchVisibility.discourage') }}
      </label>
      <p
        id="site-visibility-discourage-help"
        class="hint"
      >
        {{ t('sites.searchVisibility.discourageHelp') }}
      </p>

      <label class="checkbox">
        <input
          v-model="redirectOtherHosts"
          type="checkbox"
          :disabled="!canManage"
          aria-describedby="site-visibility-hosts-help"
        >
        {{ t('sites.searchVisibility.redirectOtherHosts') }}
      </label>
      <p
        id="site-visibility-hosts-help"
        class="hint"
      >
        {{ t('sites.searchVisibility.redirectOtherHostsHelp', { primary: primaryDisplay }) }}
      </p>

      <div class="field">
        <label for="site-visibility-robots">{{ t('sites.searchVisibility.robotsRules') }}</label>
        <textarea
          id="site-visibility-robots"
          v-model="robotsRules"
          rows="5"
          :disabled="!canManage"
          aria-describedby="site-visibility-robots-help"
        />
        <p
          id="site-visibility-robots-help"
          class="hint"
        >
          {{ t('sites.searchVisibility.robotsRulesHelp') }}
        </p>
      </div>

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>
      <p
        class="visually-hidden"
        role="status"
      >
        {{ status }}
      </p>

      <div
        v-if="canManage"
        class="actions"
      >
        <button
          ref="saveButton"
          type="submit"
          class="primary"
          :disabled="saving"
        >
          {{ t('sites.searchVisibility.save') }}
        </button>
      </div>
    </form>

    <ConfirmDialog
      v-if="confirming"
      dialog-id="site-visibility-confirm"
      :title="t('sites.searchVisibility.confirmTitle')"
      :body="t('sites.searchVisibility.confirmBody', { primary: primaryDisplay, others: otherHostNames.join(', ') })"
      :confirm-label="t('sites.searchVisibility.confirmAction')"
      :cancel-label="t('sites.searchVisibility.cancel')"
      :busy-label="t('sites.searchVisibility.saving')"
      :busy="saving"
      @confirm="save"
      @cancel="cancelConfirm"
    />
  </section>
</template>
