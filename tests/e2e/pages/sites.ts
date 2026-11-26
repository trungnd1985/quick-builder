// The sites list and a site's settings (screens `sites` and `site`).
import { expect } from '@playwright/test'
import type { TestSite } from '../fixtures/index.js'
import { Screen } from './screen.js'

export class SitesScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: 'Sites' })).toBeVisible()
    await expect(this.page.getByText('Loading sites…')).toBeHidden()
    await this.scan('sites')
  }

  async open(): Promise<void> {
    await this.page.goto(this.admin('sites'))
    await this.waitLoaded()
  }

  async expectSite(name: string): Promise<void> {
    await expect(this.page.getByRole('cell', { name, exact: true }).or(this.page.getByRole('link', { name, exact: true }))).toBeVisible()
  }

  /** The site's settings screen, from which its pages, media and trash are reached. */
  async openSite(site: TestSite): Promise<void> {
    await this.page.goto(this.admin(`sites/${site.id}`))
    await expect(this.page.getByRole('heading', { level: 1, name: site.name })).toBeVisible()
    await this.scan('site')
  }
}
