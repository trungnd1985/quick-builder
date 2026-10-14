<script setup lang="ts">
// Choosing a page of the site (024 FR-010): one select, by title and address. Template pages
// are never offered: they have no address of their own, so nothing can link to them (016
// TB-02). Shared by redirect offers (017) and menu entries (024).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

export interface PickablePage {
  id: string
  title: string
  path: string
  templateCollectionId?: string | null
}

const props = defineProps<{
  id: string
  label: string
  pages: readonly PickablePage[]
  /** A page that cannot be chosen, such as the page being taken down. */
  exceptPageId?: string
  /** The first option, when no page is chosen yet; none when omitted. */
  placeholder?: string
  describedBy?: string
}>()

/** The chosen page's id, or an empty string. */
const pageId = defineModel<string>({ required: true })
const { t } = useI18n()

const choices = computed(() => props.pages.filter((p) => p.id !== props.exceptPageId && !p.templateCollectionId))
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <select
      :id="id"
      v-model="pageId"
      :aria-describedby="describedBy"
    >
      <option
        v-if="placeholder !== undefined"
        value=""
      >
        {{ placeholder || t('menus.entry.choosePage') }}
      </option>
      <option
        v-for="page in choices"
        :key="page.id"
        :value="page.id"
      >
        {{ page.title }} ({{ page.path }})
      </option>
    </select>
  </div>
</template>
