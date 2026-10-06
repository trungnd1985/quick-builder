import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import axe from 'axe-core'
import IconButton from '../src/components/IconButton.vue'

// An icon button keeps its text: as its accessible name, and as the tooltip shown on hover
// and on keyboard focus. The icon itself is hidden from assistive technology.

describe('IconButton', () => {
  it('is named by its label, shows it as the tooltip, and hides the icon', async () => {
    const { container, emitted } = render(IconButton, { props: { icon: 'trash', label: 'Delete folder', danger: true } })

    const button = screen.getByRole('button', { name: 'Delete folder' })
    expect(button.getAttribute('data-tooltip')).toBe('Delete folder')
    expect(button.classList.contains('danger')).toBe(true)
    expect(button.textContent?.trim()).toBe('')
    expect(container.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')

    await fireEvent.click(button)
    expect(emitted().click).toHaveLength(1)

    const results = await axe.run(container, { runOnly: { type: 'rule', values: ['button-name', 'svg-img-alt'] } })
    expect(results.violations.map((v) => v.id)).toEqual([])
  })

  it('can be disabled', () => {
    render(IconButton, { props: { icon: 'pencil', label: 'Rename', disabled: true } })

    expect((screen.getByRole('button', { name: 'Rename' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
