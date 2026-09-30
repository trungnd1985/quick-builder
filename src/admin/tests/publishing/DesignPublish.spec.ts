import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import DesignTokensView from '../../src/features/design/DesignTokensView.vue'
import type { DesignPublication, DesignVersionSummary } from '../../src/api/publishing'
import en from '../../src/i18n/en.json'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'
import { i18n, SITE } from '../editor/harness'

// 012 FR-010, clarification Q1, research PR-17: the design is published on its own. Saving
// tokens changes nothing visitors see until **Publish design**; a retained version can be
// published again after confirmation.

interface Call { method: string; url: string; body?: Record<string, unknown> }

const dana = { id: 'u', displayName: 'Dana' }

function publication(liveNumber: number, hasUnpublishedChanges: boolean): DesignPublication {
  return { liveNumber, liveSinceUtc: liveNumber ? '2026-09-29T09:00:00Z' : null, liveBy: liveNumber ? dana : null, hasUnpublishedChanges }
}

function version(number: number, restoredFromNumber: number | null = null): DesignVersionSummary {
  return { number, kind: restoredFromNumber ? 'rollback' : 'publish', restoredFromNumber, publishedAtUtc: '2026-09-29T09:00:00Z', publishedBy: dana }
}

function stub(options: { canPublish?: boolean; live?: number; changes?: boolean; versions?: DesignVersionSummary[]; publishStatus?: number } = {}) {
  const calls: Call[] = []
  let tokenVersion = 3
  let document: TokenSet = JSON.parse(JSON.stringify(defaultTokens))
  let live = options.live ?? 0
  let changes = options.changes ?? true
  let versions = options.versions ?? []

  const tokens = () => ({
    siteId: SITE,
    version: tokenVersion,
    isDefault: false,
    document,
    updatedAtUtc: null,
    updatedBy: null,
    permissions: { canManage: true, canPublish: options.canPublish ?? true },
    publication: publication(live, changes),
  })

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ method, url, body })

    let status = 200
    let reply: unknown
    if (url.endsWith('/design-tokens/versions')) {
      reply = { publication: publication(live, changes), versions }
    } else if (url.endsWith('/design-tokens/publish')) {
      status = options.publishStatus ?? 200
      if (status === 200) {
        live += 1
        changes = false
        versions = [version(live), ...versions]
        reply = publication(live, false)
      } else {
        reply = { title: 'Conflict', status, errorCode: 'editor.out_of_date' }
      }
    } else if (url.endsWith('/design-tokens/rollback')) {
      live += 1
      changes = true
      versions = [version(live, body.versionNumber), ...versions]
      reply = publication(live, true)
    } else if (method === 'PUT') {
      document = body.document
      tokenVersion += 1
      changes = true
      reply = tokens()
    } else {
      reply = tokens()
    }

    return { ok: status < 300, status, statusText: 'x', headers: { get: () => null }, json: async () => reply, text: async () => JSON.stringify(reply) }
  }))
  return calls
}

async function renderView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId', name: 'site', component: { template: '<div />' } },
      { path: '/sites/:siteId/design', name: 'site-design', component: DesignTokensView },
    ],
  })
  await router.push(`/sites/${SITE}/design`)
  await router.isReady()
  const result = render(DesignTokensView, { global: { plugins: [i18n, router, createPinia()] } })
  await waitFor(() => expect(screen.queryByText(en.design.loading)).toBeNull())
  return result
}

const publishButton = () => screen.getByRole('button', { name: en.publishing.design.publish })

describe('Design publishing (012 US2)', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('says visitors see the default design, and that saved changes are not live yet', async () => {
    stub({ live: 0, changes: true })
    await renderView()

    expect(screen.getByText(en.publishing.design.defaults)).toBeTruthy()
    expect(screen.getByText(en.publishing.design.changes)).toBeTruthy()
  })

  it('publishes the saved token set version and lists the new version', async () => {
    const calls = stub({ live: 0, changes: true })
    await renderView()

    await fireEvent.click(publishButton())

    await waitFor(() => expect(screen.getByText('Live: version 1')).toBeTruthy())
    const publish = calls.find((c) => c.url.endsWith('/publish'))!
    expect(publish.url).toBe(`/api/v1/editor/sites/${SITE}/design-tokens/publish`)
    expect(publish.body).toEqual({ version: 3 })
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
    expect(screen.queryByText(en.publishing.design.changes)).toBeNull()
    expect(within(await screen.findByRole('table')).getByText('Version 1')).toBeTruthy()
  })

  it('saves unsaved edits first, then publishes what was saved', async () => {
    const calls = stub({ live: 1, changes: false, versions: [version(1)] })
    await renderView()

    const size = screen.getByLabelText(en.design.typography.baseSize)
    await fireEvent.update(size, '18')
    await fireEvent.click(publishButton())

    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/publish'))).toBe(true))
    const put = calls.findIndex((c) => c.method === 'PUT')
    const publish = calls.findIndex((c) => c.url.endsWith('/publish'))
    expect(put).toBeGreaterThanOrEqual(0)
    expect(put).toBeLessThan(publish)
    expect(calls[publish]!.body).toEqual({ version: 4 })
  })

  it('shows a stale version refusal in words', async () => {
    stub({ publishStatus: 409 })
    await renderView()

    await fireEvent.click(publishButton())

    expect(await screen.findByText(en.publishing.errors.editor.out_of_date)).toBeTruthy()
  })

  it('rolls back only after confirmation, and never to the live version', async () => {
    const calls = stub({ live: 2, changes: false, versions: [version(2), version(1)] })
    await renderView()

    await waitFor(() => expect(screen.getByRole('table')).toBeTruthy())
    const rows = within(screen.getByRole('table')).getAllByRole('row')
    expect(within(rows[1]!).queryByRole('button')).toBeNull()

    await fireEvent.click(within(rows[2]!).getByRole('button', { name: /Roll back/ }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Publish version 1 again?')
    expect(calls.some((c) => c.url.endsWith('/rollback'))).toBe(false)

    await fireEvent.click(within(dialog).getByRole('button', { name: en.publishing.confirm.confirm }))

    await waitFor(() => expect(screen.getByText('Live: version 3')).toBeTruthy())
    expect(calls.find((c) => c.url.endsWith('/rollback'))!.body).toEqual({ versionNumber: 1 })
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
  })

  it('offers neither publishing nor rollback to a member who cannot publish', async () => {
    stub({ canPublish: false, live: 2, changes: false, versions: [version(2), version(1)] })
    await renderView()

    await waitFor(() => expect(screen.getByRole('table')).toBeTruthy())
    expect(screen.queryByRole('button', { name: en.publishing.design.publish })).toBeNull()
    expect(screen.queryByRole('button', { name: /Roll back/ })).toBeNull()
  })

  it('works from the keyboard and has no accessibility violations', async () => {
    stub({ live: 2, changes: true, versions: [version(2), version(1)] })
    const { container } = await renderView()
    await waitFor(() => expect(screen.getByRole('table')).toBeTruthy())

    for (const button of [publishButton(), screen.getByRole('button', { name: /Roll back/ })]) {
      expect(button.tagName).toBe('BUTTON')
      expect(button.hasAttribute('disabled')).toBe(false)
    }

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'th-has-data-cells', 'td-headers-attr', 'heading-order'] },
    })
    expect(results.violations).toEqual([])
  })
})
