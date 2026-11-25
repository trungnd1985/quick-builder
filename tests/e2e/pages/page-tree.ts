// A site's page tree (screen `page-tree`).
import { expect } from '@playwright/test'
import type { TestSite } from '../fixtures/index.js'
import { EditorScreen } from './editor.js'
import { Screen } from './screen.js'

export class PageTreeScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: 'Pages' })).toBeVisible()
    await expect(this.page.getByText('Loading pages…')).toBeHidden()
    await this.scan('page-tree')
  }

  async open(site: TestSite): Promise<void> {
    await this.page.goto(this.admin(`sites/${site.id}/pages`))
    await this.waitLoaded()
  }

  private row(title: string) {
    return this.page.getByRole('button', { name: `Select ${title}`, exact: true })
  }

  async select(title: string): Promise<void> {
    await this.row(title).click()
    await expect(this.row(title)).toHaveAttribute('aria-pressed', 'true')
  }

  /** Adds a page through the form, under the selected page when `under` is given. */
  async createPage(title: string, slug: string, under?: string): Promise<void> {
    if (under) {
      await this.select(under)
      await this.page.getByRole('button', { name: 'Add child page' }).click()
    } else {
      await this.page.getByRole('button', { name: 'Add page' }).first().click()
    }
    const dialog = this.page.getByRole('dialog')
    await dialog.getByLabel('Title').fill(title)
    await dialog.getByLabel('Slug').fill(slug)
    await dialog.getByRole('button', { name: 'Create page' }).click()
    await expect(this.page.getByRole('status').filter({ hasText: `${title} added.` })).toBeVisible()
    await expect(this.row(title)).toBeVisible()
  }

  /** Opens the page in the editor by clicking, and scans the editor (E2E-09). */
  async openInEditor(title: string): Promise<EditorScreen> {
    await this.select(title)
    await this.page.getByRole('link', { name: 'Edit content' }).click()
    const editor = new EditorScreen(this.page, this.kind, this.scan)
    await editor.waitLoaded()
    return editor
  }

  async deletePage(title: string): Promise<void> {
    await this.select(title)
    await this.page.getByRole('button', { name: 'Delete', exact: true }).click()
    await this.page.getByRole('button', { name: 'Delete page' }).click()
    await expect(this.page.getByRole('status').filter({ hasText: `${title} deleted.` })).toBeVisible()
    await expect(this.row(title)).toBeHidden()
  }

  /** The page is in the tree, and directly under `parent` when given (one level deeper). */
  async expectPage(title: string, parent?: string): Promise<void> {
    await expect(this.row(title)).toBeVisible()
    if (parent) {
      // Each row says its level to screen readers ("Level 2"); the tree is in site order.
      const level = async (t: string) =>
        Number(
          (await this.page.getByRole('row').filter({ has: this.row(t) }).getByText(/^Level \d+$/).textContent())?.replace(/\D/g, ''),
        )
      expect(await level(title)).toBe((await level(parent)) + 1)
    }
  }
}
