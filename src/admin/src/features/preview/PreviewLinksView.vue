<script setup lang="ts">
// Every active preview link of a site, for Administrators (022 FR-015, contracts/admin-preview.md
// §3): what each shows, who created it, when it expires and was last opened, and a way to end
// one or all of them.
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { previewLinksApi, type SitePreviewLink } from '../../api/previewLinks'

const route = useRoute()
const siteId = computed(() => String(route.params.siteId))
const { t, d } = useI18n()

const links = ref<SitePreviewLink[]>([])
const cursor = ref<string | null>(null)
const loaded = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)
const confirming = ref<string | 'all' | null>(null)

const when = (iso: string | null) => (iso ? d(new Date(iso), 'short') : t('preview.share.never'))

async function load(more = false): Promise<void> {
  try {
    const page = await previewLinksApi.site(siteId.value, more ? cursor.value : null)
    links.value = more ? [...links.value, ...page.items] : page.items
    cursor.value = page.nextCursor
  } catch {
    error.value = t('preview.share.errors.generic')
  } finally {
    loaded.value = true
  }
}

onMounted(() => load())

async function revoke(id: string | 'all'): Promise<void> {
  busy.value = true
  error.value = null
  try {
    if (id === 'all') {
      await previewLinksApi.revokeAll(siteId.value)
    } else {
      await previewLinksApi.revoke(siteId.value, id)
    }
    confirming.value = null
    await load()
  } catch {
    error.value = t('preview.share.errors.generic')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="preview-links">
    <h1>{{ t('preview.links.title') }}</h1>
    <p>{{ t('preview.links.intro') }}</p>

    <p
      v-if="error"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>

    <p v-if="loaded && links.length === 0">
      {{ t('preview.links.empty') }}
    </p>

    <table v-else-if="links.length > 0">
      <thead>
        <tr>
          <th scope="col">
            {{ t('preview.share.columns.target') }}
          </th>
          <th scope="col">
            {{ t('preview.share.columns.label') }}
          </th>
          <th scope="col">
            {{ t('preview.share.columns.createdBy') }}
          </th>
          <th scope="col">
            {{ t('preview.share.columns.created') }}
          </th>
          <th scope="col">
            {{ t('preview.share.columns.expires') }}
          </th>
          <th scope="col">
            {{ t('preview.share.columns.lastOpened') }}
          </th>
          <th scope="col">
            <span class="visually-hidden">{{ t('preview.share.revoke') }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="entry in links"
          :key="entry.link.id"
        >
          <th scope="row">
            <span class="badge">{{ t(entry.link.documentKind === 'page' ? 'preview.links.kindPage' : 'preview.links.kindItem') }}</span>
            {{ entry.targetLabel ?? entry.link.documentId }}
            <span class="help">{{ entry.link.versionNumber != null ? t('preview.share.version', { number: entry.link.versionNumber }) : t('preview.share.draft') }}</span>
          </th>
          <td>{{ entry.link.label ?? '—' }}</td>
          <td>{{ entry.link.createdBy.displayName }}</td>
          <td>{{ when(entry.link.createdAtUtc) }}</td>
          <td>{{ when(entry.link.expiresAtUtc) }}</td>
          <td>{{ when(entry.link.lastOpenedAtUtc) }}</td>
          <td>
            <template v-if="confirming === entry.link.id">
              <span>{{ t('preview.share.revokeConfirm') }}</span>
              <button
                type="button"
                class="danger"
                :disabled="busy"
                @click="revoke(entry.link.id)"
              >
                {{ t('preview.share.revoke') }}
              </button>
              <button
                type="button"
                @click="confirming = null"
              >
                {{ t('trash.restore.cancel') }}
              </button>
            </template>
            <button
              v-else
              type="button"
              @click="confirming = entry.link.id"
            >
              {{ t('preview.share.revoke') }}
            </button>
          </td>
        </tr>
      </tbody>
    </table>

    <div class="actions">
      <button
        v-if="cursor"
        type="button"
        @click="load(true)"
      >
        {{ t('preview.links.loadMore') }}
      </button>
      <template v-if="links.length > 0">
        <template v-if="confirming === 'all'">
          <span>{{ t('preview.links.revokeAllConfirm', { count: links.length }) }}</span>
          <button
            type="button"
            class="danger"
            :disabled="busy"
            @click="revoke('all')"
          >
            {{ t('preview.share.revokeAll') }}
          </button>
          <button
            type="button"
            @click="confirming = null"
          >
            {{ t('trash.restore.cancel') }}
          </button>
        </template>
        <button
          v-else
          type="button"
          @click="confirming = 'all'"
        >
          {{ t('preview.share.revokeAll') }}
        </button>
      </template>
    </div>
  </section>
</template>
