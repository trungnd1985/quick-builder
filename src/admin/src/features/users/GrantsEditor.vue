<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { sitesApi, type Site } from '../../api/sites'
import type { Grants, SiteRoleName } from '../../api/identity'

// Chooses a user's roles (002 FR-020): Administrator for the whole installation, or one
// role per site. An administrator already holds every capability everywhere, so the site
// choices are set aside while that box is ticked.

const model = defineModel<Grants>({ required: true })
const { t } = useI18n()

const sites = ref<Site[]>([])
const loadError = ref(false)

onMounted(async () => {
  try {
    sites.value = await sitesApi.list()
  } catch {
    loadError.value = true
  }
})

function roleFor(siteId: string): SiteRoleName | '' {
  return model.value.siteRoles.find((r) => r.siteId === siteId)?.role ?? ''
}

function setRole(siteId: string, role: string): void {
  const others = model.value.siteRoles.filter((r) => r.siteId !== siteId)
  model.value = {
    ...model.value,
    siteRoles: role === '' ? others : [...others, { siteId, role: role as SiteRoleName }],
  }
}

function setAdministrator(administrator: boolean): void {
  model.value = { ...model.value, administrator }
}
</script>

<template>
  <fieldset class="field grants">
    <legend>{{ t('identity.grants.legend') }}</legend>

    <label class="checkbox">
      <input
        type="checkbox"
        :checked="model.administrator"
        aria-describedby="grants-admin-help"
        @change="setAdministrator(($event.target as HTMLInputElement).checked)"
      >
      {{ t('identity.grants.administrator') }}
    </label>
    <p
      id="grants-admin-help"
      class="help"
    >
      {{ t('identity.grants.administratorHelp') }}
    </p>

    <p
      v-if="loadError"
      class="error"
      role="alert"
    >
      {{ t('identity.grants.sitesUnavailable') }}
    </p>

    <p
      v-else-if="sites.length === 0"
      class="help"
    >
      {{ t('identity.grants.noSites') }}
    </p>

    <div
      v-for="site in sites"
      v-else
      :key="site.id"
      class="grant-row"
    >
      <label :for="`grant-${site.id}`">{{ site.name }}</label>
      <select
        :id="`grant-${site.id}`"
        :value="roleFor(site.id)"
        :disabled="model.administrator"
        @change="setRole(site.id, ($event.target as HTMLSelectElement).value)"
      >
        <option value="">
          {{ t('identity.grants.roles.none') }}
        </option>
        <option value="designer">
          {{ t('identity.grants.roles.designer') }}
        </option>
        <option value="editor">
          {{ t('identity.grants.roles.editor') }}
        </option>
      </select>
    </div>
  </fieldset>
</template>
