<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { pagesApi, type Component, type PageReference } from '../../api/pages'
import { pageErrorMessage } from './pageErrors'

// 010 FR-028: which pages' drafts use a component — what someone needs to know before
// deleting it, and where to go to stop using it.

const props = defineProps<{ siteId: string; component: Component }>()
const emit = defineEmits<{ close: [] }>()

const { t, te } = useI18n()

const pages = ref<PageReference[] | null>(null)
const error = ref<string | null>(null)
const heading = ref<HTMLElement | null>(null)

onMounted(async () => {
  heading.value?.focus()
  try {
    pages.value = await pagesApi.usages(props.siteId, props.component.id)
  } catch (caught) {
    error.value = pageErrorMessage(t, te, caught)
  }
})
</script>

<template>
  <section
    class="card"
    aria-labelledby="component-usages-heading"
  >
    <h2
      id="component-usages-heading"
      ref="heading"
      tabindex="-1"
    >
      {{ t('components.usages.heading', { name: component.name }) }}
    </h2>

    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>
    <p
      v-else-if="pages === null"
      class="status"
      role="status"
    >
      {{ t('components.usages.loading') }}
    </p>
    <p v-else-if="pages.length === 0">
      {{ t('components.usages.none') }}
    </p>
    <ul v-else>
      <li
        v-for="page in pages"
        :key="page.id"
      >
        <RouterLink :to="{ name: 'site-pages', params: { siteId } }">
          {{ page.title }}
        </RouterLink>
        <code>{{ page.path }}</code>
      </li>
    </ul>

    <button
      type="button"
      @click="emit('close')"
    >
      {{ t('components.usages.close') }}
    </button>
  </section>
</template>
