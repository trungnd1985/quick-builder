// 066 FR-025, SC-006 (research BB-13): a member frames and rounds blocks from the Border group
// with the keyboard alone, and a visitor sees the same border the canvas drew.
import type { Locator } from '@playwright/test'
import { expect, test } from '../fixtures/index.js'
import { DesignScreen } from '../pages/design.js'
import { EditorScreen } from '../pages/editor.js'

interface Frame {
  borderTopWidth: string
  borderTopStyle: string
  borderTopLeftRadius: string
  paddingTop: string
  boxWidth: number
  parentWidth: number
}

/** The computed border of the element holding `text`, or of its nearest `.qb-container`. */
const frameOf = (text: Locator, container = false): Promise<Frame> =>
  text.evaluate((element, outer) => {
    const framed = (outer ? element.closest('.qb-container') : element) as HTMLElement
    const style = getComputedStyle(framed)
    return {
      borderTopWidth: style.borderTopWidth,
      borderTopStyle: style.borderTopStyle,
      borderTopLeftRadius: style.borderTopLeftRadius,
      paddingTop: style.paddingTop,
      boxWidth: framed.getBoundingClientRect().width,
      parentWidth: (framed.parentElement as HTMLElement).getBoundingClientRect().width,
    }
  }, container)

const document = {
  schemaVersion: 1,
  root: {
    id: 'root',
    type: 'root',
    props: {},
    children: [{
      id: 's1',
      type: 'section',
      props: {},
      children: [{
        id: 'c1',
        type: 'container',
        props: { padding: { token: 'space-md' } },
        children: [{ id: 't1', type: 'text', props: { text: 'Framed text' }, children: [] }],
      }],
    }],
  },
}

test('a member frames and rounds blocks with the keyboard, and visitors see the same border', async ({ page, kind, scan, seed, visitor }) => {
  const site = await seed.site('Borders')
  const framed = await seed.page(site, { title: 'Framed', slug: 'framed' })
  await seed.draft(framed, document)

  await test.step('add a corner radius to the design and publish it', async () => {
    const design = new DesignScreen(page, kind, scan)
    await design.open(site)
    await expect(design.list('Corner radius').getByRole('textbox')).toHaveCount(5)
    await design.addStep('Corner radius', 'Add corner radius', 'Card', '2')
    await design.saveAndPublish()
  })

  const editor = new EditorScreen(page, kind, scan)

  await test.step('border the container with the keyboard only', async () => {
    await editor.open(site, framed)
    await editor.select(/Container/)

    // Width: None → Thin; Line style: Solid → Dashed; Corner radius: the last one, Card.
    await editor.property('Width').focus()
    await page.keyboard.press('ArrowDown')
    await expect(editor.property('Width')).toHaveValue('thin')
    await page.keyboard.press('Tab')
    await expect(editor.property('Line style')).toBeFocused()
    await page.keyboard.press('ArrowDown')
    await expect(editor.property('Line style')).toHaveValue('dashed')
    await page.keyboard.press('Tab')
    await expect(editor.property('Line colour')).toBeFocused()
    await page.keyboard.press('Tab')
    await expect(editor.property('Corner radius')).toBeFocused()
    await page.keyboard.press('End')
    await expect(editor.property('Inner spacing')).toHaveCount(0)
    await scan('editor')
  })

  await test.step('border the text, with inner spacing', async () => {
    await editor.select(/Text/)
    await editor.property('Width').focus()
    await page.keyboard.press('ArrowDown')
    await editor.property('Inner spacing').focus()
    // Unset, then the spacing steps in order: None (0), Extra small, Small.
    for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowDown')
    await expect(editor.property('Inner spacing')).toHaveValue('space-sm')
    await editor.expectSaved()
  })

  const canvasText = page.getByRole('region', { name: 'Page' }).getByText('Framed text', { exact: true })

  const onCanvas = await test.step('the canvas draws both borders', async () => {
    const box = await frameOf(canvasText, true)
    expect(box.borderTopWidth).toBe('1px')
    expect(box.borderTopStyle).toBe('dashed')
    expect(box.borderTopLeftRadius).toBe('32px')
    const text = await frameOf(canvasText)
    expect(text.borderTopWidth).toBe('1px')
    expect(text.borderTopStyle).toBe('solid')
    expect(text.paddingTop).not.toBe('0px')
    // A border never widens a block (BB-07).
    expect(text.boxWidth).toBeLessThanOrEqual(text.parentWidth)
    return { box, text }
  })

  await test.step('publish, and a visitor sees the same borders', async () => {
    await editor.publish()
    const visit = await visitor(site, 'framed')
    const published = visit.getByText('Framed text', { exact: true })
    await expect(published).toBeVisible()

    const box = await frameOf(published, true)
    expect([box.borderTopWidth, box.borderTopStyle, box.borderTopLeftRadius])
      .toEqual([onCanvas.box.borderTopWidth, onCanvas.box.borderTopStyle, onCanvas.box.borderTopLeftRadius])
    const text = await frameOf(published)
    expect([text.borderTopWidth, text.borderTopStyle, text.paddingTop])
      .toEqual([onCanvas.text.borderTopWidth, onCanvas.text.borderTopStyle, onCanvas.text.paddingTop])
    expect(text.boxWidth).toBeLessThanOrEqual(text.parentWidth)
  })
})
