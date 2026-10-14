import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import { defineComponent, h, ref } from 'vue'
import axe from 'axe-core'
import RedirectOfferCheckbox from '../../src/components/RedirectOfferCheckbox.vue'
import RedirectToPagePicker from '../../src/components/RedirectToPagePicker.vue'
import MoveDialog from '../../src/features/pages/MoveDialog.vue'
import type { PageNode } from '../../src/api/pages'
import en from '../../src/i18n/en.json'

// 017 US5 (tasks T077, T084): the redirect offer is shown and ticked only when a change moves
// live addresses; "redirect this address to…" is unticked by default; both pass axe.

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function node(id: string, path: string, parentId: string | null = null, live = true): PageNode {
  return {
    id, parentId, position: 0, depth: parentId ? 2 : 1, title: id, slug: id, path, isHome: false, childCount: 0, version: 1,
    publication: { state: live ? 'published' : 'draft', liveNumber: live ? 1 : null, liveSinceUtc: null, liveBy: null, hasUnpublishedChanges: false, liveUrl: null, pendingSchedules: [], failedSchedule: null },
    templateCollectionId: null,
  } as PageNode
}

afterEach(() => vi.unstubAllGlobals())

describe('RedirectOfferCheckbox', () => {
  it('is hidden when the change moves no live address', () => {
    render(RedirectOfferCheckbox, { props: { id: 'offer', count: 0, modelValue: true }, global: { plugins: [i18n] } })

    expect(screen.queryByRole('checkbox')).toBeNull()
  })

  it('is ticked by default and names how many addresses move', async () => {
    const Host = defineComponent(() => {
      const accepted = ref(true)
      return () => h('div', [
        h(RedirectOfferCheckbox, { id: 'offer', count: 3, modelValue: accepted.value, 'onUpdate:modelValue': (v: boolean) => (accepted.value = v) }),
        h('output', String(accepted.value)),
      ])
    })
    const { container } = render(Host, { global: { plugins: [i18n] } })

    const box = screen.getByRole<HTMLInputElement>('checkbox', { name: /3 old addresses/ })
    expect(box.checked).toBe(true)
    await fireEvent.click(box)
    expect(container.querySelector('output')?.textContent).toBe('false')
    expect((await axe.run(container)).violations.map((v) => v.id)).toEqual([])
  })
})

describe('RedirectToPagePicker', () => {
  const pages = [node('about', '/about'), node('contact', '/contact'), { ...node('post', '/blog/{slug}'), templateCollectionId: 'c' }]

  it('is unticked by default, then offers the other pages except templates', async () => {
    const chosen: (string | null)[] = []
    const { container } = render(RedirectToPagePicker, {
      props: { id: 'pick', pages, exceptPageId: 'about', modelValue: null, 'onUpdate:modelValue': (v: string | null) => chosen.push(v) },
      global: { plugins: [i18n] },
    })

    const wanted = screen.getByRole<HTMLInputElement>('checkbox', { name: en.redirects.offer.toPage })
    expect(wanted.checked).toBe(false)
    expect(screen.queryByRole('combobox')).toBeNull()

    await fireEvent.click(wanted)
    const select = screen.getByRole<HTMLSelectElement>('combobox', { name: en.redirects.offer.page })
    expect([...select.options].map((o) => o.value)).toEqual(['contact'])
    expect(chosen.at(-1)).toBe('contact')
    expect((await axe.run(container)).violations.map((v) => v.id)).toEqual([])
  })
})

describe('MoveDialog offer', () => {
  it('sends the accepted offer with a move to another parent', async () => {
    const pages = [node('about', '/about'), node('team', '/about/team', 'about'), node('contact', '/contact')]
    const { emitted } = render(MoveDialog, {
      props: { dialogId: 'move', page: pages[1]!, pages, busy: false, error: null, liveAddressCount: 1 },
      global: { plugins: [i18n] },
    })

    await fireEvent.update(screen.getByLabelText(en.pages.move.parent), 'contact')
    expect(screen.getByRole<HTMLInputElement>('checkbox').checked).toBe(true)
    await fireEvent.click(screen.getByRole('button', { name: en.pages.move.move }))

    expect(emitted().submit).toEqual([[{ parentId: 'contact', afterPageId: null, redirectOldAddresses: true }]])
  })

  it('offers nothing for a reorder among the same siblings', () => {
    const pages = [node('about', '/about'), node('team', '/about/team', 'about')]
    render(MoveDialog, { props: { dialogId: 'move', page: pages[1]!, pages, busy: false, error: null, liveAddressCount: 1 }, global: { plugins: [i18n] } })

    expect(screen.queryByRole('checkbox')).toBeNull()
  })
})
