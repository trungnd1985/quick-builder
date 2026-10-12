<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { pagesApi, type PageNode, type TemplateImpact } from '../../api/pages'
import type { CollectionSummary } from '../../api/cms'

// 016 US1, US7 (research TB-02, TB-14): make a page a collection's template, change its
// collection, or turn it back into an ordinary page. Before anything changes, the server says
// what would happen: the address pattern, item addresses gained or lost, a live address that
// stops serving the page, bindings that would break, or the rule the change would break.

const props = defineProps<{
  dialogId: string
  siteId: string
  page: PageNode
  collections: CollectionSummary[]
  /** `set` to choose a collection; `remove` to turn a template back into an ordinary page. */
  mode: 'set' | 'remove'
  busy: boolean
  error: string | null
}>()

const emit = defineEmits<{
  submit: [collectionId: string | null]
  cancel: []
}>()

const { t, te } = useI18n()

const collectionId = ref<string>(props.page.templateCollectionId ?? props.collections[0]?.id ?? '')
const impact = ref<TemplateImpact | null>(null)
const impactFailed = ref(false)
const first = ref<HTMLElement | null>(null)

const wanted = computed(() => (props.mode === 'remove' ? null : collectionId.value || null))
const unchanged = computed(() => props.mode === 'set' && wanted.value === (props.page.templateCollectionId ?? null))
const refusal = computed(() => {
  const code = impact.value?.refusal
  if (!code) return null
  const key = code.startsWith('pages.') ? `pages.errors.${code.slice('pages.'.length)}` : ''
  return key && te(key) ? t(key) : t('pages.errors.unknown')
})

async function assess(): Promise<void> {
  impact.value = null
  impactFailed.value = false
  if (props.mode === 'set' && !wanted.value) return
  try {
    impact.value = await pagesApi.templateImpact(props.siteId, props.page.id, wanted.value)
  } catch {
    impactFailed.value = true
  }
}

watch(collectionId, assess)

onMounted(async () => {
  await assess()
  await nextTick()
  first.value?.focus()
})

function submit(): void {
  if (props.mode === 'set' && (!wanted.value || unchanged.value)) return
  emit('submit', wanted.value)
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
      :aria-describedby="`${dialogId}-impact`"
      novalidate
      @keydown="onKeydown"
      @submit.prevent="submit"
    >
      <h2 :id="`${dialogId}-title`">
        {{ mode === 'set' ? t('pages.template.setTitle', { title: page.title }) : t('pages.template.removeTitle', { title: page.title }) }}
      </h2>

      <p
        v-if="mode === 'set'"
        class="help"
      >
        {{ t('pages.template.intro') }}
      </p>

      <div
        v-if="mode === 'set'"
        class="field"
      >
        <label :for="`${dialogId}-collection`">{{ t('pages.template.collection') }}</label>
        <select
          :id="`${dialogId}-collection`"
          ref="first"
          v-model="collectionId"
        >
          <option
            v-for="collection in collections"
            :key="collection.id"
            :value="collection.id"
          >
            {{ collection.name }}
          </option>
        </select>
        <p
          v-if="collections.length === 0"
          class="help"
        >
          {{ t('pages.template.noCollections') }}
        </p>
      </div>

      <div
        :id="`${dialogId}-impact`"
        aria-live="polite"
      >
        <p
          v-if="!impact && !impactFailed && (mode === 'remove' || wanted)"
          class="status"
        >
          {{ t('pages.template.assessing') }}
        </p>
        <p
          v-else-if="impactFailed"
          class="error"
        >
          {{ t('pages.template.impactFailed') }}
        </p>
        <template v-else-if="impact">
          <p
            v-if="refusal"
            class="error"
            role="alert"
          >
            {{ refusal }}
          </p>
          <template v-else>
            <p v-if="impact.pattern">
              {{ t('pages.template.pattern', { pattern: impact.pattern }) }}
            </p>
            <p
              v-if="impact.liveStaticAddressLost"
              class="warning"
              role="note"
            >
              {{ t('pages.template.liveAddressLost', { address: page.path }) }}
            </p>
            <p
              v-if="impact.itemsGainingAddress > 0"
              role="note"
            >
              {{ t('pages.template.itemsGaining', { count: impact.itemsGainingAddress }) }}
            </p>
            <p
              v-if="impact.itemsLosingAddress > 0"
              class="warning"
              role="note"
            >
              {{ t('pages.template.itemsLosing', { count: impact.itemsLosingAddress }) }}
            </p>
            <p
              v-if="impact.brokenBindings > 0"
              class="warning"
              role="note"
            >
              {{ t('pages.template.bindingsBreaking', { count: impact.brokenBindings }) }}
            </p>
          </template>
        </template>
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
          :ref="mode === 'remove' ? 'first' : undefined"
          type="button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ t('pages.template.cancel') }}
        </button>
        <button
          type="submit"
          class="primary"
          :disabled="busy || !!refusal || (mode === 'set' && (!wanted || unchanged))"
        >
          {{ busy ? t('pages.template.working') : mode === 'set' ? t('pages.template.set') : t('pages.template.remove') }}
        </button>
      </div>
    </form>
  </div>
</template>
