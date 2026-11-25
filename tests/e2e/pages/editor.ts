// The visual editor (screen `editor`; FR-009, FR-010, FR-011). Blocks are reached through
// the palette's listbox, the layers tree and the canvas's own visible text, all by role and
// name: the canvas's drop targets already say "This page is empty." and "Drop blocks here",
// so no test IDs are needed (E2E-08).

import { expect, type Locator } from '@playwright/test'
import { dragBlock } from '../fixtures/drag.js'
import type { TestPage, TestSite } from '../fixtures/index.js'
import { Screen } from './screen.js'

export class EditorScreen extends Screen {
  private get canvas(): Locator {
    return this.page.getByRole('region', { name: 'Page' })
  }

  private get layers(): Locator {
    return this.page.getByRole('tree', { name: 'Layers' })
  }

  private get saveStatus(): Locator {
    return this.page.getByRole('status').filter({ hasText: /^(All changes saved|Unsaved changes|Saving…|Not saved.*)$/ })
  }

  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('listbox', { name: 'Blocks' })).toBeVisible()
    await expect(this.canvas).toBeVisible()
    await this.scan('editor')
  }

  async open(site: TestSite, page: TestPage): Promise<void> {
    await this.page.goto(this.admin(`sites/${site.id}/pages/${page.id}/edit`))
    await this.waitLoaded()
  }

  paletteItem(name: string): Locator {
    return this.page.getByRole('listbox', { name: 'Blocks' }).getByRole('option', { name, exact: true })
  }

  /** The empty page's drop target. */
  emptyPage(): Locator {
    return this.canvas.getByText('This page is empty.')
  }

  /** The first empty slot inside a container block, such as a new Section. */
  emptySlot(): Locator {
    return this.canvas.getByText('Drop blocks here').first()
  }

  layer(name: string | RegExp): Locator {
    return this.layers.getByRole('treeitem', { name })
  }

  /** Drags a block from the palette onto `target`, and waits until the layers show one more. */
  async addBlock(name: string, target: Locator): Promise<void> {
    const before = await this.layers.getByRole('treeitem').count()
    await dragBlock(this.page, this.paletteItem(name), target, async () => {
      await expect(this.layers.getByRole('treeitem')).toHaveCount(before + 1, { timeout: 3_000 })
    })
  }

  async select(layerName: string | RegExp): Promise<void> {
    await this.layer(layerName).first().click()
    await expect(this.page.getByRole('heading', { name: 'Properties' })).toBeVisible()
  }

  property(label: string): Locator {
    return this.page.getByRole('complementary').getByLabel(label, { exact: true })
  }

  async expectSaved(): Promise<void> {
    await expect(this.saveStatus).toHaveText('All changes saved', { timeout: 20_000 })
  }

  async expectOnCanvas(text: string): Promise<void> {
    await expect(this.canvas.getByText(text, { exact: true })).toBeVisible()
  }

  /** Publishes the page from the editor's publishing group, and waits for the confirmation. */
  async publish(): Promise<void> {
    const publishing = this.page.getByRole('group', { name: 'Publishing' })
    await publishing.getByRole('button', { name: 'Publish', exact: true }).click()
    await expect(publishing.getByRole('status')).toHaveText(/^Published version \d+\.$/, { timeout: 20_000 })
  }
}
