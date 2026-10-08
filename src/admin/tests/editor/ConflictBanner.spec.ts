import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import ConflictBanner from '../../src/editor/ConflictBanner.vue'
import { i18n } from './harness'

describe('ConflictBanner', () => {
  it('asks an out-of-date admin to reload the page, not the latest draft (035 FR-020)', async () => {
    const { container } = render(ConflictBanner, {
      props: { state: { reason: 'outdated' }, reloading: false },
      global: { plugins: [i18n] },
    })

    expect(screen.getByRole('alert').textContent).toContain(en.editor.readOnly.outdated)
    expect(screen.getByRole('button', { name: en.editor.conflict.reloadPage })).toBeTruthy()
    expect(screen.queryByRole('button', { name: en.editor.conflict.reload })).toBeNull()
    expect((await axe.run(container)).violations).toEqual([])
  })
})
