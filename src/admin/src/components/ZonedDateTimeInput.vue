<script setup lang="ts">
// A moment, entered as a date and a time in the member's own time zone, which is named on
// screen; the value is the moment in UTC (ISO 8601), or null while incomplete. Extracted from
// 012's ScheduleDialog so page and item schedules share it (015 research IT-20).
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ id: string; modelValue: string | null }>()
const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()
const { t } = useI18n()

const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone

/** `YYYY-MM-DD` and `HH:MM` in local time, as date and time inputs use them. */
function localParts(iso: string | null): { date: string; time: string } {
  const date = iso ? new Date(iso) : null
  if (!date || Number.isNaN(date.getTime())) return { date: '', time: '' }
  const pad = (n: number) => String(n).padStart(2, '0')
  return {
    date: `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`,
    time: `${pad(date.getHours())}:${pad(date.getMinutes())}`,
  }
}

const initial = localParts(props.modelValue)
const date = ref(initial.date)
const time = ref(initial.time)

/** The chosen local moment in UTC, or null while the fields are incomplete. */
function toUtc(): string | null {
  if (!date.value || !time.value) return null
  const local = new Date(`${date.value}T${time.value}:00`)
  return Number.isNaN(local.getTime()) ? null : local.toISOString()
}

watch([date, time], () => emit('update:modelValue', toUtc()))
watch(() => props.modelValue, (next) => {
  if (next === toUtc()) return
  const parts = localParts(next)
  date.value = parts.date
  time.value = parts.time
})
</script>

<template>
  <div class="zoned-date-time">
    <div class="field">
      <label :for="`${id}-date`">{{ t('publishing.schedule.date') }}</label>
      <input
        :id="`${id}-date`"
        v-model="date"
        type="date"
        required
        :aria-describedby="`${id}-zone`"
      >
    </div>
    <div class="field">
      <label :for="`${id}-time`">{{ t('publishing.schedule.time') }}</label>
      <input
        :id="`${id}-time`"
        v-model="time"
        type="time"
        required
        :aria-describedby="`${id}-zone`"
      >
    </div>
    <p
      :id="`${id}-zone`"
      class="help"
    >
      {{ t('publishing.schedule.timeZone', { zone: timeZone }) }}
    </p>
  </div>
</template>
