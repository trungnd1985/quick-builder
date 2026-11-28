// The site's design tokens (screen `design`; 011 US6, 066 US3). Each token list is a group
// named by its legend, so a list is reached by role and name, and its rows by their fields.

import { expect, type Locator } from '@playwright/test'
import type { TestSite } from '../fixtures/index.js'
import { Screen } from './screen.js'

export class DesignScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: 'Design' })).toBeVisible()
    await expect(this.page.getByRole('button', { name: 'Save design' })).toBeVisible()
    await this.scan('design')
  }

  async open(site: TestSite): Promise<void> {
    await this.page.goto(this.admin(`sites/${site.id}/design`))
    await this.waitLoaded()
  }

  /** A token list, such as "Corner radius" or "Spacing". */
  list(name: string): Locator {
    return this.page.getByRole('group', { name, exact: true })
  }

  /** Adds a step to a scale list and gives it a name and a size. */
  async addStep(list: string, add: string, name: string, size: string): Promise<void> {
    const group = this.list(list)
    const before = await group.getByRole('textbox').count()
    await group.getByRole('button', { name: add }).click()
    await expect(group.getByRole('textbox')).toHaveCount(before + 1)
    await group.getByRole('textbox').last().fill(name)
    await group.getByRole('spinbutton').last().fill(size)
  }

  /**
   * Publishes the set, which saves unsaved edits first (012 research PR-17), and waits until the
   * new version is live. Clicking Save as well would race that save.
   */
  async saveAndPublish(): Promise<void> {
    await this.page.getByRole('button', { name: 'Publish design' }).click()
    await expect(this.page.getByRole('region', { name: 'Publishing' }).getByText(/^Live: version \d+$/)).toBeVisible({ timeout: 20_000 })
  }
}
