<script setup lang="ts">
// How a link to the page may look when shared (017 US1): the sharing image's name, the site,
// the sharing title and description, with their fallbacks.
import { useI18n } from 'vue-i18n'

defineProps<{ title: string; description: string; siteName: string; hasImage: boolean; id: string }>()
const { t } = useI18n()
</script>

<template>
  <section
    class="seo-preview seo-preview--sharing"
    :aria-labelledby="`${id}-heading`"
    role="region"
  >
    <h3 :id="`${id}-heading`">
      {{ t('editor.seo.sharingPreview') }}
    </h3>
    <p class="seo-preview__image">
      {{ hasImage ? t('editor.seo.withImage') : t('editor.seo.withoutImage') }}
    </p>
    <p class="seo-preview__site">
      {{ siteName }}
    </p>
    <p class="seo-preview__title">
      {{ title }}
    </p>
    <p
      v-if="description"
      class="seo-preview__description"
    >
      {{ description }}
    </p>
  </section>
</template>
