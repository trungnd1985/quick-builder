<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { setPasswordUrl, type IssuedLink } from '../../api/identity'

// Shows a set-password link once (002 FR-011, research R-10). The product sends nothing:
// the administrator copies the link and passes it on through a channel of their choosing.

const props = defineProps<{ link: IssuedLink; userName: string }>()
const emit = defineEmits<{ close: [] }>()

const { t, d } = useI18n()
const copied = ref(false)
const url = computed(() => setPasswordUrl(props.link.token))

async function copy(): Promise<void> {
  try {
    await navigator.clipboard.writeText(url.value)
    copied.value = true
  } catch {
    // Clipboard access can be refused; the link stays selectable in the field.
    copied.value = false
  }
}
</script>

<template>
  <section
    class="card"
    role="region"
    aria-labelledby="copy-link-heading"
  >
    <h2 id="copy-link-heading">
      {{ t('identity.users.link.title', { name: userName }) }}
    </h2>
    <p class="help">
      {{ t('identity.users.link.onceOnly') }}
    </p>

    <div class="field">
      <label for="copy-link-url">{{ t('identity.users.link.label') }}</label>
      <input
        id="copy-link-url"
        type="text"
        readonly
        :value="url"
        aria-describedby="copy-link-expiry"
        @focus="($event.target as HTMLInputElement).select()"
      >
      <p
        id="copy-link-expiry"
        class="help"
      >
        {{ t('identity.users.link.expires', { when: d(new Date(link.expiresAtUtc)) }) }}
      </p>
    </div>

    <p
      v-if="copied"
      role="status"
    >
      {{ t('identity.users.link.copied') }}
    </p>

    <div class="actions">
      <button
        type="button"
        class="primary"
        @click="copy"
      >
        {{ t('identity.users.link.copy') }}
      </button>
      <button
        type="button"
        @click="emit('close')"
      >
        {{ t('identity.users.link.done') }}
      </button>
    </div>
  </section>
</template>
