<script setup lang="ts">
// The item list's filters (015 FR-029): status, needs attention, schedule, and the value of
// option, boolean and reference fields, combined with "and". Built from the schema.
import { computed, inject } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../api/cms'
import type { ListQuery } from '../../../api/items'
import ReferencePicker from './ReferencePicker.vue'
import { useItemRefs } from './itemRefs'
import { ITEM_REFS } from './itemRefsKey'

const props = defineProps<{ siteId: string; fields: SchemaField[]; query: ListQuery }>()
const emit = defineEmits<{ change: [query: ListQuery] }>()
const { t } = useI18n()
inject(ITEM_REFS, () => useItemRefs(() => props.siteId), true)

const filterable = computed(() => props.fields.filter((f) => f.type === 'option' || f.type === 'boolean' || f.type === 'reference' || f.type === 'multiReference'))

function set(patch: Partial<ListQuery>): void {
  emit('change', { ...props.query, ...patch, page: 1 })
}

function setField(fieldId: string, value: string | undefined): void {
  const fields = { ...props.query.fields }
  if (value) fields[fieldId] = value
  else delete fields[fieldId]
  set({ fields })
}
</script>

<template>
  <fieldset class="item-filters">
    <legend>{{ t('cms.items.filters.label') }}</legend>

    <div class="field">
      <label for="item-filter-status">{{ t('cms.items.filters.status') }}</label>
      <select
        id="item-filter-status"
        :value="query.status ?? ''"
        @change="set({ status: (($event.target as HTMLSelectElement).value || undefined) as ListQuery['status'] })"
      >
        <option value="">
          {{ t('cms.items.filters.anyStatus') }}
        </option>
        <option
          v-for="status in ['draft', 'published', 'changed']"
          :key="status"
          :value="status"
        >
          {{ t(`cms.items.status.${status}`) }}
        </option>
      </select>
    </div>

    <div class="field">
      <label for="item-filter-scheduled">{{ t('cms.items.filters.scheduled') }}</label>
      <select
        id="item-filter-scheduled"
        :value="query.scheduled ?? ''"
        @change="set({ scheduled: (($event.target as HTMLSelectElement).value || undefined) as ListQuery['scheduled'] })"
      >
        <option value="">
          {{ t('cms.items.filters.anySchedule') }}
        </option>
        <option value="publish">
          {{ t('cms.items.filters.scheduledPublish') }}
        </option>
        <option value="unpublish">
          {{ t('cms.items.filters.scheduledUnpublish') }}
        </option>
        <option value="any">
          {{ t('cms.items.filters.scheduledAny') }}
        </option>
      </select>
    </div>

    <label class="checkbox">
      <input
        type="checkbox"
        :checked="query.attention === true"
        @change="set({ attention: ($event.target as HTMLInputElement).checked || undefined })"
      >
      {{ t('cms.items.filters.attention') }}
    </label>

    <div
      v-for="field in filterable"
      :key="field.id"
      class="field"
    >
      <template v-if="field.type === 'option' || field.type === 'boolean'">
        <label :for="`item-filter-${field.id}`">{{ field.name }}</label>
        <select
          :id="`item-filter-${field.id}`"
          :value="query.fields?.[field.id] ?? ''"
          @change="setField(field.id, ($event.target as HTMLSelectElement).value || undefined)"
        >
          <option value="">
            {{ t('cms.items.filters.anyValue') }}
          </option>
          <template v-if="field.type === 'option'">
            <option
              v-for="option in field.options ?? []"
              :key="option.id"
              :value="option.id"
            >
              {{ option.label }}
            </option>
          </template>
          <template v-else>
            <option value="true">
              {{ t('cms.items.filters.yes') }}
            </option>
            <option value="false">
              {{ t('cms.items.filters.no') }}
            </option>
          </template>
        </select>
      </template>
      <template v-else>
        <span
          :id="`item-filter-${field.id}-label`"
          class="item-field__label"
        >{{ field.name }}</span>
        <ReferencePicker
          :id="`item-filter-${field.id}`"
          :field="{ ...field, type: 'reference' }"
          :value="query.fields?.[field.id]"
          :described-by="`item-filter-${field.id}-label`"
          :invalid="false"
          :site-id="siteId"
          :item-id="null"
          @update:value="setField(field.id, $event as string | undefined)"
        />
      </template>
    </div>
  </fieldset>
</template>
