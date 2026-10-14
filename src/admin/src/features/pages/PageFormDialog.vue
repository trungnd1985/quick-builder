<script setup lang="ts">
import RedirectOfferCheckbox from '../../components/RedirectOfferCheckbox.vue'
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { isValidSlug, slugify } from './slugify'

// 010 US1 and US2: add a page or rename one. The slug is suggested from the title until
// someone edits the slug themselves; after that it is theirs. What is typed is sent as
// typed: the server's grammar decides (research P-04).

const props = defineProps<{
  dialogId: string
  heading: string
  submitLabel: string
  /** Where the page's address starts: its parent's address, or '' for the top level. */
  addressPrefix: string
  initialTitle?: string
  initialSlug?: string
  /** Offer "this is the home page" — only when adding a top-level page to a site without one. */
  offerHome?: boolean
  /** 012 FR-011: published pages whose address a slug change moves; 0 hides the warning. */
  liveAddressCount?: number
  busy: boolean
  error: string | null
}>()

const emit = defineEmits<{
  submit: [value: { title: string; slug: string; isHome: boolean; redirectOldAddresses: boolean }]
  cancel: []
}>()

const { t } = useI18n()

const title = ref(props.initialTitle ?? '')
const slug = ref(props.initialSlug ?? '')
const isHome = ref(false)
// 017 FR-028: ticked by default.
const redirectOldAddresses = ref(true)
const slugEdited = ref(props.initialSlug !== undefined)
const titleInput = ref<HTMLInputElement | null>(null)

const address = computed(() => (isHome.value ? '/' : `${props.addressPrefix}/${slug.value}`))
const slugLooksWrong = computed(() => slug.value.length > 0 && !isValidSlug(slug.value))
const movesLivePages = computed(() =>
  (props.liveAddressCount ?? 0) > 0 && props.initialSlug !== undefined && slug.value !== props.initialSlug)

watch(title, (next) => {
  if (!slugEdited.value) {
    slug.value = slugify(next)
  }
})

onMounted(async () => {
  await nextTick()
  titleInput.value?.focus()
})

function onSlugInput(): void {
  slugEdited.value = true
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <form
      class="card dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-title`"
      novalidate
      @keydown="onKeydown"
      @submit.prevent="emit('submit', { title, slug, isHome, redirectOldAddresses: movesLivePages && redirectOldAddresses })"
    >
      <h2 :id="`${dialogId}-title`">
        {{ heading }}
      </h2>

      <div class="field">
        <label :for="`${dialogId}-page-title`">{{ t('pages.form.title') }}</label>
        <input
          :id="`${dialogId}-page-title`"
          ref="titleInput"
          v-model="title"
          type="text"
          maxlength="200"
          required
        >
      </div>

      <div class="field">
        <label :for="`${dialogId}-slug`">{{ t('pages.form.slug') }}</label>
        <input
          :id="`${dialogId}-slug`"
          v-model="slug"
          type="text"
          maxlength="100"
          spellcheck="false"
          autocapitalize="off"
          required
          :aria-invalid="slugLooksWrong ? 'true' : undefined"
          :aria-describedby="`${dialogId}-slug-help`"
          @input="onSlugInput"
        >
        <p
          :id="`${dialogId}-slug-help`"
          :class="slugLooksWrong ? 'warning' : 'help'"
        >
          {{ slugLooksWrong ? t('pages.form.slugInvalid') : t('pages.form.slugHelp', { address }) }}
        </p>
      </div>

      <p
        v-if="movesLivePages"
        class="warning"
        role="note"
      >
        {{ t('publishing.address.warning', { count: liveAddressCount ?? 0 }) }}
      </p>
      <RedirectOfferCheckbox
        v-if="movesLivePages"
        :id="`${dialogId}-redirect`"
        v-model="redirectOldAddresses"
        :count="liveAddressCount ?? 0"
      />

      <div
        v-if="offerHome"
        class="field"
      >
        <label>
          <input
            v-model="isHome"
            type="checkbox"
          >
          {{ t('pages.form.isHome') }}
        </label>
      </div>

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>

      <div class="actions">
        <button
          type="button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ t('pages.form.cancel') }}
        </button>
        <button
          type="submit"
          class="primary"
          :disabled="busy"
        >
          {{ busy ? t('pages.form.saving') : submitLabel }}
        </button>
      </div>
    </form>
  </div>
</template>
