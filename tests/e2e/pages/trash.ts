// A site's trash (screen `trash`; FR-013).
import { expect } from '@playwright/test'
import type { TestSite } from '../fixtures/index.js'
import { Screen } from './screen.js'

export class TrashScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: 'Trash' })).toBeVisible()
    await expect(this.page.getByText('Loading…')).toBeHidden()
    await this.scan('trash')
  }

  async open(site: TestSite): Promise<void> {
    await this.page.goto(this.admin(`sites/${site.id}/trash`))
    await this.waitLoaded()
  }

  /** Restores a page back where it was, and waits for the confirmation. */
  async restore(label: string): Promise<void> {
    await this.page.getByRole('button', { name: `Restore ${label}`, exact: true }).click()
    const dialog = this.page.getByRole('dialog', { name: `Restore ${label}` })
    await expect(dialog.getByText(/^Back under /)).toBeVisible()
    await dialog.getByRole('button', { name: 'Restore', exact: true }).click()
    await expect(this.page.getByText(`Restored ${label}.`)).toBeVisible()
  }
}
