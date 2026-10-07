import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import ZonedDateTimeInput from '../../src/components/ZonedDateTimeInput.vue'
import { i18n } from '../cms/harness'

// 015 research IT-20: a moment entered in the member's time zone, named on screen, kept in UTC.

describe('ZonedDateTimeInput', () => {
  it('shows a UTC moment in local time, names the zone, and gives back UTC', async () => {
    const utc = '2030-06-01T07:30:00.000Z'
    const local = new Date(utc)
    const { emitted } = render(ZonedDateTimeInput, { props: { id: 'when', modelValue: utc }, global: { plugins: [i18n] } })

    const pad = (n: number) => String(n).padStart(2, '0')
    expect((screen.getByLabelText('Time') as HTMLInputElement).value).toBe(`${pad(local.getHours())}:${pad(local.getMinutes())}`)
    expect(screen.getByText(new RegExp(Intl.DateTimeFormat().resolvedOptions().timeZone.replace('/', '\\/')))).toBeTruthy()

    await fireEvent.update(screen.getByLabelText('Time'), '10:15')
    const expected = new Date(local)
    expected.setHours(10, 15, 0, 0)
    expect(emitted()['update:modelValue']!.at(-1)).toEqual([expected.toISOString()])
  })

  it('gives null while incomplete', async () => {
    const { emitted } = render(ZonedDateTimeInput, { props: { id: 'when', modelValue: null }, global: { plugins: [i18n] } })
    await fireEvent.update(screen.getByLabelText('Date'), '2030-06-01')
    expect(emitted()['update:modelValue']!.at(-1)).toEqual([null])
  })
})
