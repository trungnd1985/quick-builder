import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import HolderWarning from '../../src/editing/HolderWarning.vue'
import { i18n } from '../editor/harness'

// 023 research CE-17: publishing, deleting and copying a version into a draft someone else holds.

describe('HolderWarning', () => {
  it.each(['publish', 'delete', 'copyToDraft'] as const)('names the holder for %s', async (action) => {
    const { container } = render(HolderWarning, { props: { name: 'Anna Tran', action }, global: { plugins: [i18n] } })

    expect(screen.getByRole('note').textContent).toBe(en.editing.warnings[action].replace('{name}', 'Anna Tran'))
    expect((await axe.run(container)).violations).toEqual([])
  })
})
