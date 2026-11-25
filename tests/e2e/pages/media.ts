// A site's media library (screen `media`; FR-012).
import { expect } from '@playwright/test'
import type { TestSite } from '../fixtures/index.js'
import { Screen } from './screen.js'

export class MediaScreen extends Screen {
  async waitLoaded(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: /^Media — / })).toBeVisible()
    await expect(this.page.getByText('Loading media…')).toBeHidden()
    await this.scan('media')
  }

  async open(site: TestSite): Promise<void> {
    await this.page.goto(this.admin(`sites/${site.id}/media`))
    await this.waitLoaded()
  }

  /** Uploads through the file chooser, as a member does with "Choose files". */
  async upload(filePath: string, fileName: string): Promise<void> {
    const chooser = this.page.waitForEvent('filechooser')
    await this.page.getByRole('button', { name: 'Choose files' }).click()
    await (await chooser).setFiles(filePath)
    await expect(this.page.getByText(`${fileName}: uploaded`)).toBeVisible()
  }
}
