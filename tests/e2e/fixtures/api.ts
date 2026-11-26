// Seeding through the API the admin itself uses (specs/025-e2e-harness, E2E-04, FR-005;
// contracts/fixtures.md). Each test creates its own site under a unique host name, so tests
// never see each other's data and can run in any order and in parallel.

import type { APIRequestContext } from '@playwright/test'
import { randomBytes } from 'node:crypto'
import { KINDS, type Kind, origin, REQUEST_HEADER } from '../setup/installations.js'

export interface TestSite {
  id: string
  name: string
  hostName: string
  /** `http://<hostName>:<port><basePath>/`: the site's home page as a visitor reaches it. */
  visitorUrl: string
}

export interface TestPage {
  id: string
  title: string
  slug: string
  /** The page's address in the admin API, e.g. `/api/v1/pages/sites/<site>/pages/<page>`. */
  apiPath: string
}

/** A page document: a Section holding one Heading, as the editor saves it. */
export function headingDocument(text: string, level = 1): object {
  return {
    schemaVersion: 1,
    root: {
      id: 'root',
      type: 'root',
      props: {},
      children: [
        {
          id: 's1',
          type: 'section',
          props: {},
          children: [{ id: 'h1', type: 'heading', props: { text, level }, children: [] }],
        },
      ],
    },
  }
}

export class Seed {
  constructor(
    private readonly request: APIRequestContext,
    private readonly kind: Kind,
  ) {}

  private url(path: string): string {
    return `${origin(this.kind)}${path}`
  }

  private async ok<T>(response: Awaited<ReturnType<APIRequestContext['get']>>, what: string): Promise<T> {
    if (!response.ok()) {
      throw new Error(`${what} failed (${response.status()}): ${await response.text()}`)
    }
    const body = await response.text()
    return (body ? JSON.parse(body) : undefined) as T
  }

  /** A site of its own, under a host name no other test uses (E2E-05). */
  async site(name = 'E2E site'): Promise<TestSite> {
    const hostName = `s-${randomBytes(4).toString('hex')}.e2e.test`
    const created = await this.ok<{ id: string }>(
      await this.request.post(this.url('/api/v1/sites'), {
        headers: REQUEST_HEADER,
        data: { name, hostNames: [hostName] },
      }),
      `Creating site ${hostName}`,
    )
    const { port, basePath } = KINDS[this.kind]
    return { id: created.id, name, hostName, visitorUrl: `http://${hostName}:${port}${basePath}/` }
  }

  async page(site: TestSite, page: { title: string; slug: string; parentId?: string; isHome?: boolean }): Promise<TestPage> {
    const pages = `/api/v1/pages/sites/${site.id}/pages`
    const created = await this.ok<{ id: string }>(
      await this.request.post(this.url(pages), {
        headers: REQUEST_HEADER,
        data: { title: page.title, slug: page.slug, parentId: page.parentId ?? null, isHome: page.isHome ?? false },
      }),
      `Creating page ${page.slug}`,
    )
    return { id: created.id, title: page.title, slug: page.slug, apiPath: `${pages}/${created.id}` }
  }

  /** Saves `document` as the page's draft and returns the draft's new version. */
  async draft(page: TestPage, document: object): Promise<number> {
    const draft = this.url(`${page.apiPath}/draft`)
    const current = await this.ok<{ version: number }>(await this.request.get(draft), `Reading ${page.slug}'s draft`)
    const saved = await this.ok<{ version: number }>(
      await this.request.put(draft, { headers: REQUEST_HEADER, data: { version: current.version, document } }),
      `Saving ${page.slug}'s draft`,
    )
    return saved.version
  }

  async publish(page: TestPage, draftVersion: number): Promise<void> {
    await this.ok(
      await this.request.post(this.url(`${page.apiPath}/publish`), { headers: REQUEST_HEADER, data: { draftVersion } }),
      `Publishing ${page.slug}`,
    )
  }

  /** A home page, published, showing `heading` in an `<h1>`. */
  async publishedPage(site: TestSite, heading: string): Promise<TestPage> {
    const home = await this.page(site, { title: 'Home', slug: 'home', isHome: true })
    await this.publish(home, await this.draft(home, headingDocument(heading)))
    return home
  }
}
